# ADR-0009: AppSync Resolver Selection — Direct DynamoDB Resolvers as Default, Lambda Restricted to Complex Logic

## Status
**Proposed** — June 2026

---

## Context

[ADR-0007](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md) adopted AWS AppSync as the client-facing API and established the heuristic *"DynamoDB-direct for reads, Lambda for logic"* in its Section 2 resolver table. That rule was introduced as part of a larger adoption decision and focused primarily on query fields (`products`, `product`, `categories`, `basket`).

As the GraphQL schema grows to cover mutations (`createProduct`, `deleteProduct`, `updateProduct`, simple basket writes), teams face recurring classification questions with no explicit, standalone policy:

- When does a mutation qualify as "simple" enough for a direct resolver?
- What is the minimum complexity that justifies routing a field through a Lambda?
- How is the boundary enforced when the default instinct (from the existing .NET/MediatR background) is to write a Lambda handler for everything?

Without a codified rule, the practical outcome is **Lambda sprawl**: cold starts and `.NET` handler overhead accumulate for fields that do nothing but write a single item to DynamoDB. This silently reverses the cost and latency gains that motivated ADR-0007.

---

## Decision

**Direct DynamoDB resolvers (APPSYNC_JS) are the default for all AppSync fields** — queries and mutations alike. Lambda resolvers are a deliberate **escalation**, required only when the field's logic cannot be expressed as a single DynamoDB operation with a resolver mapping.

### 1. The Default Rule

Every new AppSync field MUST start as a **direct DynamoDB resolver**. The author must provide an explicit justification to escalate it to Lambda. The absence of a justification means it stays direct.

### 2. Escalation Criteria — When Lambda Is Required

A field MUST be a Lambda resolver if and only if it satisfies **at least one** of the following:

| Criterion | Examples |
|-----------|---------|
| **Complex business validation** — logic beyond what the schema enforces; invariants that require reading state to compute a result | Discount eligibility per item; conditional write that depends on prior read's output |
| **Multi-service orchestration** — the field must invoke another Lambda or microservice to complete its response | `storeBasket` → Discount Lambda invoke per cart item |
| **External integration** — any call to a non-DynamoDB, non-AppSync service (EventBridge publish, third-party HTTP) | Inline EventBridge publish; webhook calls |
| **Cross-aggregate transactions** — `TransactWriteItems` spanning more than one aggregate root with coordination logic | Order reservation across two tables with conditional writes |

If none of these apply, **use a direct resolver**.

> **Cache is NOT an escalation criterion.** Redis/cache-aside in the resolver path does not
> justify Lambda. For high-read fields, use AppSync server-side caching instead — it operates
> at the API layer with no Lambda invocation cost. A cache layer inside a Lambda resolver
> compounds cold-start latency and complexity without improving cache hit semantics.
> The only exception is a cache that is **write-coupled to business state** (e.g. invalidating
> a distributed cache as part of a transactional write) — in that case, the write coupling
> itself is the escalation reason, not the cache.

### 3. Resolver Decision Table

Apply the following criteria for each field before implementation:

**Catalog / Basket / Discount (current AppSync schema)**

| Field | Direct or Lambda? | Escalation criterion |
|---|---|---|
| `products(query, sortBy, minRating, maxRating, pageSize, nextToken)` | **Lambda** | **Criterion 3** — external, non-DynamoDB integration (OpenSearch). Reclassified from Direct by [ADR-0027](./0027-catalogview-opensearch-product-search-and-rating-sync.md); invokes CatalogView's `SearchProducts` |
| `product(id)` | **Lambda** | **Criterion 3** — external, non-DynamoDB integration (OpenSearch). Reclassified from Direct by [ADR-0027](./0027-catalogview-opensearch-product-search-and-rating-sync.md); invokes CatalogView's `GetProduct` |
| `categories(pageSize, nextToken)` | **Direct** | — |
| `basket(userName)` | **Direct** | — (Redis removed from read path; AppSync cache for performance) |
| `createProduct(input)` | **Direct** | — |
| `updateProduct(input)` | **Direct** | — (`attribute_exists` condition replaces read-before-write) |
| `deleteProduct(id)` | **Direct** | — |
| `deleteBasket(userName)` | **Direct** | — |
| `storeBasket(input)` | **Lambda** | **Criterion 2** — invokes Discount Lambda per cart item |
| `checkoutBasket(input)` | **Lambda** | **Criterion 1** — 2-step DynamoDB pipeline: UpdateItem with CDC marker derived from input + DeleteItem; state-writing sequence for DynamoDB Streams (ADR-0008) |
| `couponFor(productName)` | **Lambda** | **Criterion 1** — Discount domain rule: GetItem + business fallback (zero-discount coupon) |

**Ordering (added to AppSync schema)**

| Field | Direct or Lambda? | Escalation criterion |
|---|---|---|
| `orders(pageSize, nextToken)` | **Direct** | — Scan with `#Type = :type` filter; no cross-service calls |
| `ordersByName(name, pageSize, nextToken)` | **Direct** | — Scan with `contains(OrderName, :name)` + Type filter |
| `ordersByCustomer(customerId)` | **Lambda** | **Criterion 1** — GSI1 projection is KEYS_ONLY; Query returns only key attributes, requiring a subsequent BatchGetItem for full order data (2 operations) |
| `deleteOrder(orderId)` | **Lambda** | **Criterion 1** — Query ORDERITEM rows then TransactWriteItems (delete order + items atomically); 2-step write sequence not expressible as a single DynamoDB operation |

### 4. Lambda Resolver Mapping Files

Every AppSync Lambda resolver field MUST have a corresponding APPSYNC_JS resolver file at
`graphql/resolvers/<Type>.<field>.js`. The file documents the AppSync → Lambda payload
contract and is used by AppSync in production. Format:

```js
// resolvers/Query.ordersByCustomer.js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { CustomerId: ctx.args.customerId },  // maps GraphQL args to Lambda request shape
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { items: ctx.result.Orders.map(o => ({ ... })), nextToken: null }  // maps Lambda response to GraphQL type
}
```

The local dev route handler (`app/api/graphql/local.ts`) mirrors the same payload format via
`invokeLambda` calls, ensuring parity between dev and production AppSync behavior.

### 5. Implementation Pattern

**Correct** — a conditional update uses a direct DynamoDB resolver (no Lambda needed):

```js
// resolvers/Mutation.updateProduct.js
import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const { id, name, description, imageUrl, price, stock, categoryIds } = ctx.args.input
  return {
    operation: 'UpdateItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    update: {
      expression: 'SET #Name = :name, Description = :desc, ImageUrl = :img, Price = :price, Stock = :stock, CategoryIds = :cats',
      expressionNames: { '#Name': 'Name' },
      expressionValues: {
        ':name': util.dynamodb.toDynamoDB(name),
        ':desc': util.dynamodb.toDynamoDB(description),
        ':img': util.dynamodb.toDynamoDB(imageUrl),
        ':price': util.dynamodb.toDynamoDB(price),
        ':stock': util.dynamodb.toDynamoDB(stock),
        ':cats': util.dynamodb.toStringSet(categoryIds),
      },
    },
    condition: { expression: 'attribute_exists(Id)' },
  }
}

export function response(ctx) {
  if (ctx.error) {
    if (ctx.error.type === 'DynamoDB:ConditionalCheckFailedException') {
      util.error('Product not found', 'NOT_FOUND')
    }
    util.error(ctx.error.message, ctx.error.type)
  }
  return { id: ctx.args.input.id }
}
```

**Incorrect** — wrapping a plain `DeleteItem` in a Lambda just to keep the .NET handler:

```csharp
// ⛔ A Lambda with MediatR pipeline for what is a single DeleteItem —
// adds cold start, .NET allocation, and unnecessary complexity.
[LambdaFunction]
[HttpApi(LambdaHttpMethod.Delete, "/catalog/products/{id}")]
public async Task<IResult> DeleteProduct(
    string id, ISender sender, CancellationToken ct)
    => TypedResults.Ok(await sender.Send(new DeleteProductCommand(id), ct));
```

**Incorrect** — adding Redis cache-aside inside a Lambda resolver to "speed up" a read:

```csharp
// ⛔ Cold start + Redis round-trip + DynamoDB read — worse latency than a direct resolver
// with AppSync server-side caching enabled.
[LambdaFunction]
public async Task<GetBasketResponse?> GetBasket(GetBasketRequest request, ...)
{
    var cached = await redis.GetAsync(request.UserName);      // Redis round-trip
    if (cached != null) return cached;
    var basket = await basketRepository.GetBasket(request.UserName, ct);  // DynamoDB
    await redis.SetAsync(request.UserName, basket);
    return basket.Adapt<GetBasketResponse>();
}
// Use AppSync server-side caching on the field instead: zero cold start, no Redis cost.
```

### 5. What This Rule Does NOT Cover

- **CDC / EventBridge integration events**: resolver type has no bearing on event publishing. Integration events are still emitted by DynamoDB Streams → publisher Lambda → EventBridge ([ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)). Resolvers MUST NOT publish integration events inline.
- **Existing Lambda HTTP endpoints** (`[HttpApi]` functions): those are internal surfaces not consumed by the SPA. This rule applies only to AppSync resolver classification.
- **Lambda handlers retained for internal/diagnostic use**: ADR-0007 allows existing REST endpoints to remain for non-SPA consumers; their existence does not violate this policy.

---

## Applies To

- `src/WebApps/Shopping.Web.SPA.React/graphql/resolvers/` — all current and future APPSYNC_JS resolvers.
- `src/Services/Catalog/Catalog.Function` — `createProduct`, `updateProduct`, `deleteProduct` reclassified to direct resolvers and decommissioned from AppHost (implemented).
- `src/Services/Basket/Basket.Function` — `basket` and `deleteBasket` reclassified to direct resolvers and decommissioned; `storeBasket` and `checkoutBasket` remain Lambda (criteria 1 and 2 apply).
- `src/Services/Discount/Discount.Function` — `couponFor` remains Lambda (criterion 1: Discount domain logic).
- `src/Services/Ordering/Ordering.Function` — `orders` and `ordersByName` reclassified to direct resolvers (decommissioned from AppHost); `ordersByCustomer` and `deleteOrder` remain Lambda resolvers (criterion 1 applies to both).
- `src/Services/CatalogView/CatalogView.Function` — `products` and `product` reclassified **from** direct resolvers **to** Lambda (criterion 3: OpenSearch integration), per [ADR-0027](./0027-catalogview-opensearch-product-search-and-rating-sync.md).

---

## Consequences

### Positive

- **Eliminates unnecessary Lambda cold starts** for simple mutations (`createProduct`, `updateProduct`, `deleteProduct`), which account for the majority of write operations in the Catalog service.
- **Reduces operational surface** — fewer Lambda functions to deploy, monitor, and version for fields that require no logic.
- **Clear default** — engineers start with direct and escalate; the burden of justification is on complexity, not on simplicity.
- **Cost reduction** — direct resolvers have no Lambda invocation cost; billing is only the AppSync request and the DynamoDB read/write unit.

### Negative / Costs

- **Two resolver runtimes to maintain** — the team writes APPSYNC_JS for direct resolvers and `.NET` for Lambda resolvers. Cognitive context-switching between the two.
- **APPSYNC_JS limitations** — direct resolvers cannot express arbitrarily complex response transformations; occasionally a field that seems simple has an edge-case shaping requirement that only Lambda can handle cleanly.
- **Local development gap** — APPSYNC_JS resolvers are harder to test locally than .NET Lambda handlers (weaker Aspire/LocalStack emulation for AppSync, as noted in ADR-0007 Consequences).

### Mitigation Strategies

- Keep all APPSYNC_JS resolver logic minimal: request mapping + error check + return. Any non-trivial shaping that strains the resolver signals it should be Lambda.
- Use a deployed dev AppSync API or the local GraphQL stub (ADR-0007 Mitigation) for resolver development, rather than expecting full local parity.
- Review the escalation-criteria table in §2 at PR time as a checklist; the reviewer confirms the classification before merge.

### Future Constraints

- New AppSync fields MUST be classified against the §2 escalation criteria before implementation. An undocumented Lambda resolver for a field that has no escalation justification is a policy violation.
- If a direct resolver proves insufficient after implementation (edge case discovered in production), it MAY be escalated to Lambda via a PR that documents which escalation criterion now applies — no new ADR required for a per-field reclassification.
- Adding a new escalation criterion (i.e., a new category of complexity that justifies Lambda) requires updating this ADR.

---

## References

- [ADR-0007: AWS AppSync (GraphQL) as the Client-Facing API — Direct DynamoDB Resolvers for Reads, Lambda for Business Logic](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC)](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0004: AWS-First Messaging — EventBridge over MassTransit/RabbitMQ](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- AWS AppSync — JavaScript (APPSYNC_JS) resolvers, direct DynamoDB data sources
