---
name: resolver-selection
description: Use when deciding whether an AppSync field should use a direct DynamoDB resolver or a Lambda resolver — e.g. "which resolver for this mutation?", "should this be a direct resolver?", "implement a direct DynamoDB resolver for X", "does this field need Lambda?", "classify this AppSync field". Applies ADR-0009's rule: direct DynamoDB (APPSYNC_JS) is the default for all fields; Lambda is an explicit escalation required only when at least one of four criteria is met.
---

# AppSync resolver selection skill

Classifies an AppSync field (query or mutation) and implements the correct resolver type,
following [ADR-0009](../../../docs/adr/0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md).

**The rule in one sentence**: direct DynamoDB resolver is the default; Lambda is an escalation
that requires an explicit justification.

## Escalation criteria — when Lambda is required

A field MUST be a Lambda resolver if and only if it satisfies **at least one** of these:

| # | Criterion | Concrete examples in DuckStore |
|---|-----------|-------------------------------|
| 1 | **Complex business validation** — rules beyond schema types; invariants that require reading state to compute a result | Discount eligibility per item; `checkoutBasket` CDC pipeline |
| 2 | **Multi-service orchestration** — must invoke another Lambda or microservice | `storeBasket` → Discount Lambda invoke per cart item |
| 3 | **External integration** — any call to a non-DynamoDB, non-AppSync service (EventBridge publish, third-party HTTP) | Inline EventBridge publish; webhook calls |
| 4 | **Cross-aggregate transaction** — `TransactWriteItems` spanning multiple aggregate roots with coordination logic | Order reservation across two tables with conditional writes |

If **none** of the four apply → use a direct DynamoDB resolver.

> **Cache (Redis) is NOT an escalation criterion.** Use AppSync server-side caching for
> high-read fields instead — zero cold start, no Redis round-trip. A Lambda with Redis
> cache-aside is always slower than a direct resolver with AppSync caching enabled at the
> field level.

## Decision flow

Work through the questions in order and stop at the first "yes":

1. Does this field call another Lambda or downstream microservice? → **Lambda** (criterion 2)
2. Does this field call EventBridge, a third-party HTTP service, or any non-DynamoDB external service? → **Lambda** (criterion 3). **Note:** Redis/cache alone does NOT trigger this — use AppSync server-side caching instead.
3. Does this field need state-dependent business rules that read DynamoDB to compute the response (not just for validation)? → **Lambda** (criterion 1). A `ConditionExpression` that checks existence is NOT state-dependent business logic — use `attribute_exists` in a direct resolver instead.
4. Does this field coordinate a `TransactWriteItems` across more than one aggregate root? → **Lambda** (criterion 4)
5. None of the above? → **Direct DynamoDB resolver**

## Current field classification (DuckStore)

### Catalog / Basket / CatalogView

| AppSync field | Resolver | Criterion |
|---|---|---|
| `products(query, sortBy, minRating, maxRating, pageSize, nextToken)` | **Direct** | — Query on CatalogView's GSI1 when no free-text `query`; Scan+filter only for substring text search (unavoidable — same tradeoff ADR-0030 already accepts) |
| `product(id)` | **Direct** | — |
| `categories(pageSize, nextToken)` | **Direct** | — |
| `basket(ownerId)` | **Direct** | — |
| `createProduct(input)` / `updateProduct(input)` / `deleteProduct(id)` | **Direct** | — (`attribute_exists` condition covers not-found) |
| `storeBasket(ownerId, input)` | **Direct** | — plain PutItem, no business logic (the old Discount-Lambda-per-item invoke was removed by ADR-0012 — don't reintroduce it as a reason to keep this Lambda) |
| `deleteBasket(ownerId)` | **Direct** | — |
| `checkoutBasket(input)` | **Lambda** | **Criterion 1** — existence guard + UpdateItem (CDC marker) + DeleteItem; kept as Lambda pending further work (see basket checkout notes) |
| `mergeBasket(guestId)` | **Lambda** | **Criterion 1** — `ShoppingCart.Merge` sums quantities across two carts in code (not expressible as an `UpdateExpression`), then a same-table `TransactWriteItems` |

### Pricing

| AppSync field | Resolver | Criterion |
|---|---|---|
| `nominalPriceFor(productId)` | **Direct** | — |
| `currentDiscountForProduct(productId)` | **Direct** | — GetItem + read-time expiry check |
| `setNominalPrice(productId, price, cost)` | **Direct** | — UpdateItem upsert; the `prices` item only ever persists a single `UpdatedAt`, so no prior read is needed |
| `setGatewayCost(provider, ...)` | **Direct** | — same single-`UpdatedAt` pattern as `setNominalPrice` |
| `installmentPlanFor(productId)` | **Lambda** | **Criterion 1** — reads Price + GatewayCost + Campaign discount (3 tables) and runs `InstallmentCalculator` (tiered interest/margin math) |
| `basketInstallmentPlan(items)` | **Lambda** | **Criterion 1** — same calculation, summed across the whole cart |
| `createCampaign(...)` | **Lambda** | **Criterion 4** — `TransactWriteItems` across `campaigns` + N `product-discounts` rows |
| `endCampaign(campaignId)` | **Lambda** | **Criterion 4** — reads the campaign, then a fan-out `TransactWriteItems` retracting its `product-discounts` rows |

### User

| AppSync field | Resolver | Criterion |
|---|---|---|
| `myProfile` | **Direct** | — UpdateItem with `if_not_exists(Email, …)`/`if_not_exists(Name, …)` replicates lazy get-or-create provisioning (ADR-0017) with no Lambda |
| `updateProfile(input)` | **Direct** | — |

### Ordering

| AppSync field | Resolver | Criterion |
|---|---|---|
| `orders(pageSize, nextToken)` | **Direct** | — Scan + `#Type = :type` filter |
| `ordersByName(name, pageSize, nextToken)` | **Direct** | — Scan + `contains(OrderName)` + Type filter (substring search — same accepted tradeoff as `products`) |
| `ordersByCustomer(customerId)` | **Direct** | — Query on GSI1 (`ProjectionType.ALL` avoids a follow-up GetItem) |
| `deleteOrder(orderId)` | **Direct** | — DeleteItem |

### Review

| AppSync field | Resolver | Criterion |
|---|---|---|
| `reviewsByProduct(productId, pageSize, nextToken)` | **Direct** | — Query on GSI1 |
| `createReview(input)` | **Direct** | — pipeline resolver (GetItem existence check → PutItem upsert) |

**When re-auditing this table**: don't trust a row until you've re-read the actual handler/resolver
— this table has gone stale before (e.g. `ordersByCustomer`/`deleteOrder` were marked Lambda here
long after they'd already become direct resolvers in code). Verify against
`graphql/resolvers/<domain>/{queries,mutations}/*.js` and `infra/constructs/appsync-api.ts` directly.

## Implementation patterns

### Resolver file organization

Resolvers are organized by bounded context, then by operation kind:

```
graphql/resolvers/<domain>/<queries|mutations>/<TypeName>.<fieldName>.js
```

`<domain>` mirrors the bounded contexts in `src/Services/*` as seen from the API surface:
`basket`, `products`, `categories`, `pricing`, `orders`, `reviews`, `user`. `<queries|mutations>`
is derived straight from `TypeName` (`Query` → `queries`, `Mutation` → `mutations`). A pipeline
resolver's step files (e.g. `Mutation.createReview.checkExisting.js`) live in the same
`<domain>/mutations/` folder as their parent file.

`infra/constructs/appsync-api.ts` is the map from GraphQL field to file: every
`this.resolver(dataSource, id, typeName, fieldName, domain)` /
`this.pipelineResolver(dataSource, id, typeName, fieldName, domain, steps)` call passes the
`domain` string explicitly — that argument is what points at the folder. When adding a field,
put its `.js` file in the matching domain folder first, then wire it with that same `domain`
string in `appsync-api.ts`.

### Direct DynamoDB resolver (APPSYNC_JS)

File location: `src/WebApps/Shopping.Web.SPA.React/graphql/resolvers/<domain>/queries/<TypeName>.<fieldName>.js` (or `mutations/` for `Mutation` fields)

**Query — Scan (paginated list)**
```js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const { pageSize = 10, nextToken } = ctx.args
  return { operation: 'Scan', limit: pageSize, nextToken }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { items: ctx.result.items, nextToken: ctx.result.nextToken }
}
```

**Query — GetItem (single item)**
```js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { id: util.dynamodb.toDynamoDB(ctx.args.id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.result
}
```

**Mutation — PutItem (create)**
```js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const id = util.autoId()
  return {
    operation: 'PutItem',
    key: { id: util.dynamodb.toDynamoDB(id) },
    attributeValues: util.dynamodb.toMapValues({ ...ctx.args.input, id }),
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.result
}
```

**Mutation — UpdateItem**
```js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const { id, ...fields } = ctx.args.input
  const sets = Object.keys(fields).map(k => `#${k} = :${k}`)
  return {
    operation: 'UpdateItem',
    key: { id: util.dynamodb.toDynamoDB(id) },
    update: {
      expression: `SET ${sets.join(', ')}`,
      expressionNames: Object.fromEntries(Object.keys(fields).map(k => [`#${k}`, k])),
      expressionValues: util.dynamodb.toMapValues(
        Object.fromEntries(Object.keys(fields).map(k => [`:${k}`, fields[k]]))
      ),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.result
}
```

**Mutation — DeleteItem**
```js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'DeleteItem',
    key: { id: util.dynamodb.toDynamoDB(ctx.args.id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.args.id }
}
```

### Lambda resolver mapping file (APPSYNC_JS)

Every Lambda resolver field MUST have a resolver file at
`graphql/resolvers/<domain>/queries|mutations/<Type>.<field>.js` documenting the AppSync → Lambda
payload contract:

```js
// resolvers/pricing/queries/Query.installmentPlanFor.js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { ProductId: ctx.args.productId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { maxInstallmentsWithoutInterest: ctx.result.MaxInstallmentsWithoutInterest, ... }
}
```

The local dev handler (`local.ts`) mirrors the same payload shape via `invokeLambda`.

### Lambda resolver (handler)

Lambda resolvers follow the existing `.NET` handler pattern in `src/Services/*/`:
- `[LambdaFunction]` on a method in `partial class Functions`
- DI wired in `Startup.ConfigureServices`
- MediatR command/query via `ISender.Send(...)`
- AppSync passes `AppSyncResolverEvent` as the Lambda event payload

For the AppSync schema, declare the field normally; the data source binding is configured
in the AppSync CDK/SAM definition, not in the resolver file.

## Correct vs incorrect

**Correct** — a simple delete uses a direct resolver (no Lambda cold start):
```js
// resolvers/products/mutations/Mutation.deleteProduct.js
export function request(ctx) {
  return { operation: 'DeleteItem', key: { id: util.dynamodb.toDynamoDB(ctx.args.id) } }
}
export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.args.id }
}
```

**Incorrect** — wrapping a plain DeleteItem in a Lambda just to keep the .NET handler:
```csharp
// ⛔ Full .NET Lambda + MediatR pipeline for a single DeleteItem — cold start for nothing.
[LambdaFunction]
[HttpApi(LambdaHttpMethod.Delete, "/catalog/products/{id}")]
public async Task<IResult> DeleteProduct(string id, ISender sender, CancellationToken ct)
    => TypedResults.Ok(await sender.Send(new DeleteProductCommand(id), ct));
```

## What NOT to do

- **Do not** add a new Lambda resolver for a field that satisfies none of the four escalation criteria.
- **Do not** use Redis/cache-aside inside a Lambda resolver to "speed up" a read field — the cold start + Redis round-trip is always slower than a direct resolver with AppSync server-side caching enabled at the field level.
- **Do not** route integration events through AppSync resolvers — that path is DynamoDB Streams → EventBridge (ADR-0005). Resolvers MUST NOT call `IEventPublisher` inline.
- **Do not** use VTL — all direct resolvers in this project use APPSYNC_JS.

## What to read before classifying a field

1. Check `src/WebApps/Shopping.Web.SPA.React/graphql/schema.graphql` — the field's argument
   and return types tell you what data it touches.
2. If an existing Lambda handler exists under `src/Services/*/Features/`, read it to see
   whether it actually uses Redis, cross-service calls, or complex validation — those are
   the escalation criteria, not the mere existence of the handler.
3. Check `src/WebApps/Shopping.Web.SPA.React/graphql/resolvers/<domain>/` to see whether a
   resolver file already exists for the field.

## After classifying and implementing

- Add the resolver file under `graphql/resolvers/<domain>/queries|mutations/<Type>.<field>.js`
  (direct) or confirm the Lambda handler project exists under `src/Services/*/` (Lambda). If the
  field's domain folder doesn't exist yet, create it — don't bolt the field onto an unrelated one.
- Update `schema.graphql` if the field is new.
- Document which escalation criterion applies (or confirm none do) in the PR description —
  the reviewer checks this as part of ADR-0009 compliance.
- Per ADR-0007, generate or update TypeScript types from `schema.graphql` after schema changes.
