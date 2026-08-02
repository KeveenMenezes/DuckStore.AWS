---
tags:
  - status/accepted
  - domain/ordering
---

# ADR-0010: Collapse Ordering into a Single Function Project with a Single-Item DynamoDB Model

## Status
**Accepted** — June 2026

---

## Context

`Ordering` was the last service still shaped by the pre-serverless architecture
(EF Core + PostgreSQL + layered DDD). After the migrations in [ADR-0004](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
and [ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md), its structure was
inconsistent with every other .NET service and carried relational leftovers that DynamoDB
makes unnecessary:

- **Project layout.** `Ordering` was split across `Ordering.Domain`, `Ordering.Application`,
  `Ordering.Infrastructure`, `Ordering.Function`, plus two standalone Lambda projects
  (`Ordering.BasketCheckoutConsumer.Lambda`, `Ordering.OrderCreatedPublisher.Lambda`).
  `Catalog`, `Basket`, and `Discount` are each a single `*.Function` project, and both
  Catalog and Basket already moved their CDC stream publishers **inside** the Function project.
- **Table-per-entity persistence.** An order was stored as a composite single-table model
  (`PK=ORDER#{id}, SK=ORDER` for the header + one `SK=ORDERITEM#{itemId}` row per line item),
  written atomically with `TransactWriteItems` and reassembled from multiple rows with a
  `Query`. This mirrors relational table joins, not DynamoDB aggregate modeling. The other
  services store an aggregate as **one item** with its collection embedded (Basket cart items,
  Catalog `CategoryIds`) and write it with a single `PutItem`.
- **N+1 reads.** `GetOrdersByCustomer` queried GSI1 (`KEYS_ONLY`) and then issued a `GetItem`
  per result to rehydrate each order.
- **Dead code.** `GetOrders` (Scan-based pagination + a Scan `COUNT`), `GetOrdersByName`
  (Scan `contains`), `GetOrdersByStatus` (+ a second GSI), and `UpdateAsync`/`Order.Update`
  had no Lambda endpoint wired in `OrderingExtensions` — only `GetOrdersByCustomer` and
  `DeleteOrder` (plus the consumer/publisher) are exposed. The unused query paths existed
  only to serve handlers with no route.

This is a structural domain/persistence decision affecting a bounded context, so it warrants
an ADR. It also supersedes the "Ordering keeps the classic DDD/layered structure" note
previously documented in `CLAUDE.md`.

---

## Decision

Reshape `Ordering` to match the serverless-native pattern used by the other services.

### 1. One `Ordering.Function` project

Collapse `Ordering.Domain` + `Ordering.Application` + `Ordering.Infrastructure` + the two
`*.Lambda` projects into a single `Ordering.Function` library (namespaces `Ordering.Function.*`),
laid out like `Catalog.Function` (`Models/`, `ValueObjects/`, `Enums/`, `Features/`,
`Repositories/`, `Dtos/`, `EventsIntegration/`). DDD types (`Order` aggregate, value objects,
MediatR CQRS slices) are **retained inside** this project — the change is packaging, not
abandoning the domain model. `Ordering.DevelopmentDataSeeder` stays a separate Worker and now
references `Ordering.Function`.

The two event Lambdas become plain classes inside the Function project, referenced by explicit
handler strings in `OrderingExtensions` (same approach as Catalog/Basket):

- `EventsIntegration/Consumer/BasketCheckoutConsumerFunction`
- `EventsIntegration/Publisher/OrderCreatedPublisherFunction`

### 2. One DynamoDB item per order, with embedded line items

An order MUST be persisted as a **single item** keyed by a simple `Id` (HASH) primary key,
with `OrderItems` stored as an embedded list attribute. A single `PutItem` is atomic on its
own, so `TransactWriteItems` is NOT used. Reads are a `GetItem` by `Id`; the per-customer
listing uses **one** GSI (`GSI1PK=CUSTOMER#{id}`, `GSI1SK=CreatedAt`) with
`ProjectionType.ALL`, so a query returns full orders without a follow-up `GetItem` per result.
The `Type="Order"` attribute is retained as the DynamoDB Streams filter discriminator for the
`OrderCreated` publisher (per ADR-0005).

```
Correct (single item, embedded items, one PutItem):
  { Id, Type:"Order", CustomerId, OrderName, Status, CreatedAt,
    GSI1PK, GSI1SK, ShippingAddress:{...}, Payment:{...},
    OrderItems: [ { Id, ProductId, Quantity, Price }, ... ] }

Incorrect (relational table-per-entity, TransactWriteItems):
  { PK:"ORDER#id", SK:"ORDER", ... }
  { PK:"ORDER#id", SK:"ORDERITEM#a", ... }
  { PK:"ORDER#id", SK:"ORDERITEM#b", ... }
```

### 3. Remove the dead read/write paths

`GetOrders` (pagination), `GetOrdersByName`, `GetOrdersByStatus` (and its GSI2), and
`UpdateAsync`/`Order.Update` are removed along with their tests. The repository surface is
reduced to what is actually reachable: `GetByIdAsync`, `AddAsync`, `DeleteAsync(Guid)`,
`GetOrdersByCustomerAsync`, and `AnyAsync` (existence check for the seeder, matching
`DynamoProductRepository.AnyAsync`). If pagination/search/status listing is needed later, it
MUST be (re)introduced deliberately against this single-item model — not restored as Scans.

---

## Applies To

- `src/Services/Ordering/Ordering.Function` (new single project)
- `src/Services/Ordering/Ordering.DevelopmentDataSeeder` (now references `Ordering.Function`)
- `src/AppHost/OrderingExtensions.cs`, `src/AppHost/AppHost.csproj`, `DuckStore.sln`
- `tests/Services/Ordering/Ordering.UnitTests`

Removed: `Ordering.Domain`, `Ordering.Application`, `Ordering.Infrastructure`,
`Ordering.BasketCheckoutConsumer.Lambda`, `Ordering.OrderCreatedPublisher.Lambda`.

---

## Consequences

### Positive
- `Ordering` is now structurally identical to `Catalog`/`Basket`/`Discount` — one fewer mental
  model and matching CDC-publisher-in-Function convention.
- Writes are simpler and cheaper: one `PutItem` instead of `TransactWriteItems` over N+1 rows;
  reads are a single `GetItem`, and per-customer listing has no N+1.
- Less code and fewer projects to build/maintain; no misleading unreachable query paths.

### Negative / Costs
- The single-item model is bounded by the DynamoDB **400 KB item size limit**; an order with
  an extreme number of line items would not fit (acceptable for this domain/sample).
- Loses the project's value as a "classic layered DDD" reference sample (a deliberate trade
  for serverless-native consistency, continuing ADR-0005's direction).
- The dev `OrderingTable` schema changed (simple key, GSI1 `ALL`, no GSI2); the local table is
  recreated by the seeder, so no data migration is involved in dev. A real deployment would
  require a table rebuild/backfill.

### Mitigation Strategies
- Keep DDD types (`Aggregate<TId>` markers, value objects, MediatR slices) inside the Function
  project so the domain boundary remains explicit despite the flatter packaging.
- Keep the `Type="Order"` discriminator and Streams configuration so the ADR-0005 CDC pipeline
  is unaffected by the model change.

---

## References
- [ADR-0004: AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC)](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0008: Extend CDC Event Publishing to the Basket ShoppingCarts Stream Publisher](./0008-extend-cdc-event-publishing-basket-shoppingcarts-stream-publisher.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
