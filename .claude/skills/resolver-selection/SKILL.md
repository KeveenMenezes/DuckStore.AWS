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

### Catalog / Basket / Discount (AppSync schema active)

| AppSync field | Resolver | Criterion |
|---|---|---|
| `products(pageSize, nextToken)` | **Direct** | — |
| `product(id)` | **Direct** | — |
| `categories(pageSize, nextToken)` | **Direct** | — |
| `basket(userName)` | **Direct** | — (Redis removed; AppSync field caching for perf) |
| `createProduct(input)` | **Direct** | — |
| `updateProduct(input)` | **Direct** | — (`attribute_exists` condition covers not-found) |
| `deleteProduct(id)` | **Direct** | — |
| `deleteBasket(userName)` | **Direct** | — |
| `storeBasket(input)` | **Lambda** | **Criterion 2** — Discount Lambda invoked per cart item |
| `checkoutBasket(input)` | **Lambda** | **Criterion 1** — UpdateItem with CDC marker payload + DeleteItem; stateful write pipeline for DynamoDB Streams (ADR-0008) |
| `couponFor(productName)` | **Lambda** | **Criterion 1** — Discount domain rule: GetItem + business fallback (zero-discount coupon) |

### Ordering (AppSync schema active)

| AppSync field | Resolver | Criterion |
|---|---|---|
| `orders(pageSize, nextToken)` | **Direct** | — Scan + `#Type = :type` filter |
| `ordersByName(name, pageSize, nextToken)` | **Direct** | — Scan + `contains(OrderName)` + Type filter |
| `ordersByCustomer(customerId)` | **Lambda** | **Criterion 1** — GSI1 is KEYS_ONLY; 2-step read (Query + BatchGetItem) |
| `deleteOrder(orderId)` | **Lambda** | **Criterion 1** — 2-step write (Query ORDERITEM rows + TransactWriteItems) |

## Implementation patterns

### Direct DynamoDB resolver (APPSYNC_JS)

File location: `src/WebApps/Shopping.Web.SPA.React/graphql/resolvers/<TypeName>.<fieldName>.js`

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

Every Lambda resolver field MUST have a resolver file at `graphql/resolvers/<Type>.<field>.js`
documenting the AppSync → Lambda payload contract:

```js
// resolvers/Query.ordersByCustomer.js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { CustomerId: ctx.args.customerId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { items: ctx.result.Orders.map(o => ({ ... })), nextToken: null }
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
// resolvers/Mutation.deleteProduct.js
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
3. Check `src/WebApps/Shopping.Web.SPA.React/graphql/resolvers/` to see whether a resolver
   file already exists for the field.

## After classifying and implementing

- Add the resolver file under `graphql/resolvers/<Type>.<field>.js` (direct) or confirm
  the Lambda handler project exists under `src/Services/*/` (Lambda).
- Update `schema.graphql` if the field is new.
- Document which escalation criterion applies (or confirm none do) in the PR description —
  the reviewer checks this as part of ADR-0009 compliance.
- Per ADR-0007, generate or update TypeScript types from `schema.graphql` after schema changes.
