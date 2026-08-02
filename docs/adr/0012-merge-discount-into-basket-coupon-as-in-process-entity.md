---
tags:
  - status/superseded
  - domain/basket
---

# ADR-0012: Merge Discount into Basket — Coupon as an In-Process Entity of the ShoppingCart Aggregate

## Status
**Superseded** — July 2026, by [ADR-0026](./0026-pricing-bounded-context-price-and-campaign-ownership.md)
for §1/§2 ("`Coupon` lives in Basket as an in-process entity") — `Coupon`/discount ownership moves
to a new Pricing service. This ADR's core prohibition (no synchronous per-item cross-service
Lambda invoke) remains in force and is explicitly reaffirmed by ADR-0026 §3.

---

## Context

`Discount` was a standalone `Discount.Function` Lambda (plus `Discount.DevelopmentDataSeeder`)
backed by a single DynamoDB table (`coupons`) and exposing one operation: `GetDiscount`. Its
only synchronous consumer was `Basket`, which called it during `StoreBasket` via the AWS Lambda
Invoke API (`IDiscountClient`/`DiscountLambdaClient`) — one invoke per cart item to deduct the
coupon amount from each item's price.

That topology added cost without buying separation:

- **Per-item cross-service invoke.** A cart with N items triggered N synchronous Lambda
  invocations during a single `StoreBasket`, each paying invoke latency + cold-start risk, to
  perform what is fundamentally a `GetItem` by product name.
- **A whole service for one key lookup.** `Discount.Function` existed only to wrap a DynamoDB
  `GetItem`. It had no independent write API, no events, and no other consumer. The discount
  rule ("item price is reduced by the coupon amount") is a **Basket** concern — it mutates the
  cart — yet lived behind a network hop.
- **Anemic domain.** `ShoppingCart`/`Coupon` were plain DTO classes; the discount loop lived in
  the handler. `BuildingBlocks.Core.DomainModel` already provides `Aggregate<TId>`/`Entity<TId>`
  used by `Ordering`, but `Basket` did not use them.
- **Frontend coupling.** The AppSync/GraphQL `couponFor` field was a **Lambda resolver** that
  invoked `discount-get-discount` — a Lambda escalation that [ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
  reserves for genuine complexity, not a key lookup.
- **Extra wiring.** AppHost carried `DiscountExtensions`, a `WithLambdaInvokeTarget` helper, the
  `Discount__FunctionName` env var, and `AWSSDK.Lambda` in `Basket.Function`.

This is a structural domain (bounded-context) and service-to-service-communication decision, so
it warrants an ADR. It revises the "Basket → Discount via direct Lambda invoke" pattern recorded
in `CLAUDE.md` and in [ADR-0010](./0010-collapse-ordering-into-single-function-single-item-model.md)
(which referenced `Discount` as a peer single-Function service).

---

## Decision

Fold the Discount capability into `Basket` and model it with the shared DDD base types. No
network hop, no separate service.

### 1. `ShoppingCart` is the aggregate root; `Coupon` is an in-process entity

`ShoppingCart` MUST extend `Aggregate<string>` (Id = `UserName`, the `shopping-carts` partition
key) and own the discount rule. `Coupon` MUST be an `Entity<string>` (Id = product name, the
`coupons` partition key). The discount application is domain behavior on the aggregate, not a
loop in the handler:

```csharp
// Correct — aggregate owns the rule; coupons resolved in-process.
public void ApplyDiscounts(IEnumerable<Coupon> coupons)
{
    var couponsByProduct = coupons.ToDictionary(c => c.Id);
    foreach (var item in Items)
        if (couponsByProduct.TryGetValue(item.ProductName, out var coupon))
            item.ApplyDiscount(coupon.Amount);
}
```

```csharp
// Incorrect — per-item cross-service Lambda invoke in the handler.
foreach (var item in command.Cart.Items)
{
    var coupon = await discountClient.GetDiscountAsync(item.ProductName, ct);
    item.Price -= coupon.Amount;
}
```

`StoreBasketCommandHandler` resolves coupons through `ICouponRepository` (a DynamoDB `GetItem`,
falling back to `Coupon.NoDiscountFor(productName)`) and calls `cart.ApplyDiscounts(...)`. The
coupon repository (`ICouponRepository`/`DynamoCouponRepository`, table `coupons`) and `Coupon`
model live inside `Basket.Function` (`Data/`, `Models/`).

### 2. The `coupons` table is owned and seeded by Basket

`Basket.DevelopmentDataSeeder` creates and seeds both the `shopping-carts` and `coupons` tables.
The standalone `Discount.DevelopmentDataSeeder` is removed.

### 3. `couponFor` becomes a direct DynamoDB resolver

Per [ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md), the
GraphQL `couponFor` field MUST be a **direct DynamoDB resolver** (`GetItem` on `coupons` by
`ProductName`) — a key lookup needs no Lambda. The local GraphQL mock (`app/api/graphql/local.ts`)
and the deploy artifact (`graphql/resolvers/Query.couponFor.js`) both do a direct `GetItem`.
`storeBasket` remains a Lambda resolver (MediatR/FluentValidation pipeline), but its rationale is
no longer "Discount Lambda invoke per item".

### 4. Remove the obsolete service and wiring

`Discount.Function`, `Discount.DevelopmentDataSeeder`, `Basket.Function/Clients/DiscountClient.cs`,
`AppHost/DiscountExtensions.cs`, the `WithLambdaInvokeTarget` AppHost helper (its only use), the
`Discount__FunctionName` env var, and the `AWSSDK.Lambda` package reference in `Basket.Function`
are all removed. The unused `ICouponRepository.UpdateAsync`/`DeleteAsync` (never reachable) are
dropped; the surface is `GetByProductNameAsync`, `AnyAsync`, `AddAsync`.

---

## Applies To

- `src/Services/Basket/Basket.Function` (aggregate/entity, coupon repository, handler, startup)
- `src/Services/Basket/Basket.DevelopmentDataSeeder` (now also provisions/seeds `coupons`)
- `src/AppHost` (`Program.cs`, `BasketExtensions.cs`, `Extensions/Extensions.cs`, `AppHost.csproj`)
- `src/WebApps/Shopping.Web.SPA.React` (`couponFor` resolver + schema comments)
- `DuckStore.slnx`

Removed: `src/Services/Discount/Discount.Function`, `src/Services/Discount/Discount.DevelopmentDataSeeder`.

---

## Consequences

### Positive
- `StoreBasket` is a single in-process flow: N cheap `GetItem`s (no per-item Lambda invoke, no
  cold-start tax) instead of N cross-service invocations.
- The discount rule lives where it belongs — on the `ShoppingCart` aggregate that it mutates —
  and `Basket` now uses the same `Aggregate<TId>`/`Entity<TId>` base types as `Ordering`.
- One fewer service, seeder, AppHost extension, and SDK dependency; `couponFor` aligns with
  ADR-0009 (direct-first resolvers).

### Negative / Costs
- Discount can no longer scale, deploy, or fail independently of Basket (it never did in
  practice — Basket was its only caller; acceptable for this sample).
- Couponing logic is now coupled to the Basket deployment unit; a future, richer Discount
  domain (campaigns, rules engine, its own events) would need to be re-extracted.
- [ADR-0010](./0010-collapse-ordering-into-single-function-single-item-model.md) and `CLAUDE.md`
  reference `Discount` as a peer service; those mentions are now historical.

### Mitigation Strategies
- Keep `Coupon` as a distinct `Entity<string>` with its own table and repository so the
  sub-domain boundary stays explicit and re-extraction remains mechanical.
- Should Discount need to become a service again, reintroduce it deliberately (its own
  Function + events), not by restoring per-item synchronous Lambda invokes.

---

## References
- [ADR-0009: AppSync Resolver Selection — Direct-First, Lambda for Complex Logic](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0010: Collapse Ordering into a Single Function Project with a Single-Item DynamoDB Model](./0010-collapse-ordering-into-single-function-single-item-model.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC)](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
