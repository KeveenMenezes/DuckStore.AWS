# ADR-0026: Pricing Bounded Context — Price and Campaign Ownership Move from Catalog/Basket

## Status
**Proposed** — July 2026

---

## Context

Price and discount data is split across two services that should not own it:

- **Catalog** stores `Product.Price` as a plain `decimal` — no history, no vigência (validity
  window), no distinction between a list price and a promotional price.
- **Basket** stores `Coupon` (an in-process `Entity<string>` keyed by product name, per
  [ADR-0012](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md)) — a flat
  per-unit deduction with no campaign grouping, no vigência, no percentage option.

This produces exactly the divergence the business wants to eliminate: the storefront (Catalog)
and the cart (Basket) can each apply a different notion of "the price" for the same product,
there is no way to batch-activate a set of discounts for an event (e.g. Black Friday), and there
is no installment-payment calculation anywhere in the codebase.

We are introducing a new **Pricing** bounded context — `src/Services/Pricing/Pricing.Function` —
to become the single source of truth for: nominal price, campaigns/discounts (fixed or
percentage, with automatic vigência), and installment engineering (max interest-free
installments for a given price). Product traceability is not a separate feature here — it falls
out naturally from Pricing being keyed by `ProductId`.

Removing `Coupon` from Basket revisits ADR-0012, so this ADR must be explicit about what it
supersedes and what it reaffirms.

---

## Decision

### 1. Price ownership moves from Catalog to Pricing

`Product.Price` is **removed** from Catalog's `Product` aggregate (`Create`/`Update`/`Load`, the
DynamoDB item, the AppSync schema, and its four JS resolvers). `Pricing.Function` owns a `Price`
aggregate keyed by `ProductId`, in a new `prices` table (PK `ProductId`).

### 2. Coupon/discount capability moves from Basket to Pricing — supersedes ADR-0012 §1/§2

[ADR-0012](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md) merged `Coupon`
into Basket, modeled as an in-process `Entity<string>`. That ruling (§1 "`Coupon` is an
in-process entity of `ShoppingCart`" and §2 "the `coupons` table is owned and seeded by Basket")
is **superseded**: `Coupon`, `ICouponRepository`/`DynamoCouponRepository`, the `coupons` table,
and the `couponFor` resolver are removed entirely from Basket. Basket now has **zero** discount
responsibility — `ShoppingCart`/`ShoppingCartItem` no longer expose `ApplyDiscounts`/
`ApplyDiscount`; `StoreBasketCommandHandler` persists the cart exactly as submitted.

ADR-0012's own mitigation strategy anticipated this: *"Should Discount need to become a service
again, reintroduce it deliberately (its own Function + events), not by restoring per-item
synchronous Lambda invokes."* This ADR is that deliberate reintroduction.

### 3. ADR-0012's core prohibition is reaffirmed, not superseded

ADR-0012's central objection to the old `Discount.Function` was a **synchronous per-item
cross-service Lambda invoke** from Basket during `StoreBasket` — N invocations per cart, each
paying cold-start/latency risk for what is fundamentally a `GetItem`. That prohibition stands:
Basket **MUST NOT** call Pricing synchronously (Lambda Invoke or otherwise) from any of its
Lambdas. Basket's write path has no coupling to Pricing at all now, not even an async one — it
simply stores whatever price/quantity the caller submits. A future read-aggregation service
(**CatalogView**, not built in this ADR) is the intended consumer of Pricing's data for display;
if it needs cart-time pricing, it queries Pricing directly, never through Basket.

### 4. Product creation and price initialization are two decoupled calls

`createProduct` (Catalog, AppSync direct DynamoDB resolver) no longer accepts a `price` argument
and creates the product without one. A separate mutation, `setNominalPrice(productId, price)`
(Pricing, Lambda-backed per ADR-0009 — it validates a monetary value server-side), establishes or
updates the price. This is a deliberate two-step flow orchestrated by the client, **not** an
oversight: it avoids any cross-service synchronous call or cross-table write from a single
resolver, and mirrors the reality that pricing may be edited by a different actor than the one
who enters catalog data.

```
Correct  — two independent calls, no coupling between the two writes:
  1. createProduct(input)        → Catalog PutItem, no Price attribute
  2. setNominalPrice(id, price)  → Pricing PutItem, Pricing.Function::SetNominalPrice

Incorrect — Catalog's resolver reaching into Pricing's table, or a synchronous invoke:
  createProduct(input) → PutItem into "products" AND PutItem into "prices" in one resolver
```

### 5. Pricing consumes `CatalogUpdatedEvent` for cleanup only

When a product is deleted, Catalog's existing stream publisher emits `CatalogUpdatedEvent`
(`ChangeType == "REMOVE"`) via CDC ([ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)).
Pricing's `CatalogProductRemovedConsumer` subscribes to this (EventBridge rule filtered on
`detailType: ['CatalogUpdatedEvent']`, filtered further on `ChangeType` inside the handler) and
deletes its own `prices` and `product-discounts` rows for that `ProductId`, idempotently via the
standard inbox pattern (`pricing-processed-events`, one `TransactWriteItems` per delivery). This
is the only cross-service coupling Pricing has, and it is async/CDC — never synchronous.

### 6. Campaign fan-out is a plain in-handler `TransactWriteItems`, not a stream rule

A `Campaign` (aggregate, table `campaigns`, PK `Id`) groups a discount by event for batch
activation across N products (business rule: campaigns/cupons por evento). Creating one denormalizes
a `product-discounts` row (PK `ProductId`) per enrolled product, so a per-product discount lookup
stays a single `GetItem`:

```csharp
// DynamoCampaignRepository.AddAsync — one TransactWriteItems, no CDC round-trip
var items = new List<TransactWriteItem> { Put(campaigns, campaign) };
items.AddRange(campaign.ProductIds.Select(id => Put(productDiscounts, id, campaign)));
await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = items });
```

This deliberately does **not** use the [ADR-0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
stream-rule/dispatcher pattern: that pattern exists for cross-service CDC (a table's Streams
feeding an EventBridge publish). Fanning `campaigns` out to `product-discounts` is an internal
projection **within** Pricing — a plain transactional write in the same Lambda invocation is
simpler and stronger (no eventual-consistency window) than a Streams round-trip for a
same-service concern. `campaigns` has no DynamoDB Streams enabled. `EndCampaign` runs the inverse
transaction: mark the campaign cancelled, delete each of its `product-discounts` rows.

### 7. Discount vigência is enforced at read time, not by a scheduler

`StartsAt`/`EndsAt` on a campaign/discount are compared against `now` inside the AppSync JS
resolver's `response()` function (`util.time.nowISO8601()`), not by any cron/EventBridge
Scheduler/Step Functions job that flips a status. A discount past `EndsAt` is simply treated as
absent by the reader. No scheduled infrastructure is introduced by this ADR.

### 8. One active campaign per product (simplifying assumption)

A product MAY be enrolled in only one campaign's `product-discounts` row at a time in this
delivery — there is no stacking or tie-break logic for overlapping campaigns. `CreateCampaign`
overwrites any existing `product-discounts` row for a given product. Multi-campaign stacking is
out of scope.

### 9. Installment engineering is a pure calculation, not a table

> **Superseded by [ADR-0028](./0028-gateway-cost-table-and-payment-highlights.md).** Installment
> engineering now cross-references a real `gateway-costs` table (one row per provider, one active
> at a time) instead of a single flat env-var-driven formula. `MinMarginPercent` remains a
> store-level setting as described below; only the per-installment-count fee source changed.

`GetInstallmentPlan` (Lambda, `Query.installmentPlanFor` — a calculation, not a key lookup, so it
escalates per [ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md))
reads a product's nominal price and applies a simulated gateway fee/margin formula sourced from
`Installments__MaxInstallments` / `Installments__FeePerInstallmentPercent` /
`Installments__MinMarginPercent` environment configuration — not a DynamoDB table. There is no
real payment-gateway integration in this repo (mirroring the simulated-gateway rationale of
[ADR-0025](./0025-payment-bounded-context-simulated-gateway-cdc.md)), so a small configurable
formula is sufficient and avoids introducing an admin UI/table for gateway rates that nothing
else needs yet.

### 10. CatalogView is a named future consumer, not built here

A future service, **CatalogView**, is expected to become the read-side aggregation layer joining
Catalog + Pricing for storefront display. It is explicitly **out of scope** for this ADR — noted
here only so Pricing's read-side shape (`prices`/`product-discounts`, both plain `GetItem` by
`ProductId`) is not revisited when CatalogView is eventually designed.

---

## Applies To

- New: `src/Services/Pricing/Pricing.Function`, `src/Services/Pricing/Pricing.DevelopmentDataSeeder`
- `src/Services/Catalog/Catalog.Function` (`Product` entity, `DynamoProductRepository`, JS
  resolvers, dev seeder) — `Price` removed
- `src/Services/Basket/Basket.Function`, `Basket.DevelopmentDataSeeder` — `Coupon` and all
  discount logic removed
- `src/AppHost` (`PricingExtensions.cs`, `Program.cs`, `AppHost.csproj`)
- `infra/` (`pricing-dynamodb.ts`, `pricing-lambdas.ts`, `pricing-stack.ts`, `bin/app.ts`,
  `basket-dynamodb.ts`, `basket-lambdas.ts`, `basket-stack.ts`, `appsync-api.ts`)
- `src/WebApps/Shopping.Web.SPA.React` (`graphql/schema.graphql`, `graphql/resolvers/*.js`,
  `graphql/types.ts`, `app/api/graphql/local.ts`, `features/products/services/products.service.ts`)
- `DuckStore.slnx`

---

## Consequences

### Positive
- One source of truth for price and discount data — the storefront and the cart can no longer
  disagree about what a product costs.
- Campaigns can be batch-activated/deactivated across N products atomically, with automatic
  vigência and no scheduled infrastructure.
- Basket is simpler: zero discount responsibility, zero coupling to Pricing (sync or async).
- Installment engineering exists for the first time, isolated in one pure, testable calculator.

### Negative / Costs
- Product creation becomes a two-step flow (`createProduct` then `setNominalPrice`) instead of
  one — a minor UX cost for the admin/seller flow.
- Until CatalogView exists, the storefront's product list/detail queries must fetch price via a
  second query per product (`nominalPriceFor`), an N+1 pattern acceptable for this demo's scale
  but not something to carry into a higher-traffic context.
- One campaign per product at a time is a real limitation — genuine discount-stacking scenarios
  are not supported.
- `TransactWriteItems` caps at 100 items, so a single campaign is limited to 99 enrolled products;
  not enforced in code, acceptable for a demo.

### Mitigation Strategies
- Keep `prices`/`product-discounts` as plain `ProductId`-keyed tables so CatalogView (or any
  future reader) can query them directly without any resolver/schema rework.
- If discount stacking becomes a real requirement, extend `product-discounts` to a
  multi-item-per-product shape (adjacency list) rather than overloading the single-row projection.

---

## References
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- [ADR-0005: Remove Domain Events — CDC via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First, Lambda for Complex Logic](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0012: Merge Discount into Basket — Coupon as an In-Process Entity](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md)
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
- [ADR-0025: Payment Bounded Context — Simulated Gateway via Fully Async EventBridge/CDC](./0025-payment-bounded-context-simulated-gateway-cdc.md)
- [ADR-0028: Gateway Cost Table and Payment Highlights](./0028-gateway-cost-table-and-payment-highlights.md)
