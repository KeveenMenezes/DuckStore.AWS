# ADR-0028: Gateway Cost Table and Payment Highlights — Installment/À Vista Pricing via CDC

## Status
**Proposed** — July 2026

---

## Context

Pricing's installment engine ([ADR-0026](./0026-pricing-bounded-context-price-and-campaign-ownership.md)
§9) computes the maximum number of interest-free installments from a single flat, linear formula
(`InstallmentOptions.FeePerInstallmentPercent` applied uniformly to every installment count) and
has no concept of a "payment provider." This does not reflect how real processors (Stripe,
Pagar.me) actually price installments — their fee tables vary per installment count and per
provider, and they also offer a distinct, usually lower rate for upfront (à vista) payment.

Separately, CatalogView ([ADR-0027](./0027-catalogview-opensearch-product-search-and-rating-sync.md))
already denormalizes Pricing's nominal price onto its OpenSearch search document via CDC
(`PriceChangedEvent` → `PriceStreamPublisher` → `PriceSyncConsumer`), so the storefront can read
price without a runtime join. There is no equivalent for payment highlights (installment badge, à
vista price) — showing them today would require a synchronous calculation per page view, which is
exactly what CatalogView's CDC-denormalization pattern exists to avoid.

This ADR introduces a `gateway-costs` table in Pricing (one row per provider, only one "active" at
a time), rewrites the installment engine to cross-reference it, adds a computed à vista price, and
propagates both into CatalogView's search document alongside the existing price sync.

---

## Decision

### 1. `gateway-costs` table — one row per provider, one active at a time

New table `gateway-costs`, PK `Provider` (string). One item per payment-gateway provider:

```
Provider              S   "Simulated"
FlatFeePerTransaction N   "0.39"
AvistaRatePercent     N   "2.5"
InstallmentRates      M   { "1": N"3.0", "2": N"4.2", ..., "12": N"14.0" }
UpdatedAt             S   ISO-8601
```

`InstallmentRates` is a DynamoDB Map keyed by stringified installment count — a direct point
lookup, no scan, and a natural fit for AppSync's `AWSJSON` scalar on the write side
(`setGatewayCost`). Only **one provider is active at a time**
(`Installments:ActiveProvider` configuration, default `"Simulated"`) — there is no multi-provider
comparison/selection logic. `gateway-costs` has **no DynamoDB Stream**: see §5.

`Modules/GatewayCosts` (new, sibling to `Modules/Prices`/`Modules/Campaigns`) owns `GatewayCost`
(`Aggregate<GatewayProvider>`), `IGatewayCostRepository`/`DynamoGatewayCostRepository`, and the
`setGatewayCost` Lambda mutation (validates non-negative rates, non-empty rate table — same
ADR-0009 escalation rationale as `setNominalPrice`: server-side validation of a monetary value,
not a plain key lookup).

### 2. Installment engine reads the real cost table — supersedes ADR-0026 §9's flat formula

`InstallmentOptions` drops `MaxInstallments`/`FeePerInstallmentPercent`; it now holds only
`ActiveProvider` and `MinMarginPercent`. **`MinMarginPercent` stays a store-level setting, not a
`GatewayCost` property** — it is the merchant's own risk tolerance, not a gateway cost, so the two
concerns remain deliberately separate.

`InstallmentCalculator.CalculateInstallmentPlan` walks the active provider's actual
`InstallmentRates` keys (not an assumed `1..12` range) and returns the highest installment count N
whose cumulative fee (`FlatFeePerTransaction + nominalPrice * rate[N]/100`) still leaves at least
`MinMarginPercent` of the nominal price as margin (`marginBasedLimit`).

The cap is **hybrid**, not margin-only: a second, independent signal — `tierBasedLimit` — is looked
up from a new `InstallmentOptions.ValueTiers` list (`ValueTier(MinAmount, MaxInstallments)`,
ascending by `MinAmount`) against `originalPrice`, which doubles as the caller's total basket value
for both the single-product flow (`GetInstallmentPlanHandler`, a "cart of one") and the real
basket flow (`GetBasketInstallmentPlanHandler`, the summed cart total). The final cap is
`finalLimit = Math.Max(marginBasedLimit, tierBasedLimit)`, capped to the highest installment count
the active `GatewayCost.InstallmentRates` actually offers — a tier can unlock more installments than
margin alone would allow (e.g. a large multi-unit cart), but it can never pull the cap *below* what
margin already grants. `ValueTiers` is configured the same way as `MinMarginPercent` — manually
parsed, gap-stopping, defensive `Installments__ValueTiers__{index}__MinAmount` /
`__MaxInstallments` env vars in `InstallmentOptions.FromConfiguration` — and defaults to an empty
list, which reduces the hybrid cap to today's pure-margin behavior.

### 3. À vista price is a computed value, not a label

`InstallmentCalculator.CalculateAvistaPrice` returns
`nominalPrice * (1 - AvistaRatePercent/100)`. This is a **demo/simulated simplification**: the à
vista rate is modeled as a discount passed 100% to the customer (pay upfront, the gateway charges
the merchant less, and the store passes all of that saving on) — not a real
gateway-cost-passthrough policy, where a merchant might keep some of that margin instead. Both
`GetInstallmentPlanResult`/`Response` (the synchronous `installmentPlanFor` query) and the
CatalogView-bound event (§4) carry `AvistaPrice` alongside the installment fields.

### 4. Payment highlights ride the existing `PriceChangedEvent` — one event, one merge

`PriceStreamPublisherFunction` (Pricing), on every non-`REMOVE` `prices` stream record, publishes
a **single** `PriceChangedEvent`, extended with the three payment-highlight fields rather than
introducing a second, parallel event for the same trigger:

```csharp
public record PriceChangedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public decimal NominalPrice { get; init; }
    public decimal AvistaPrice { get; init; }
    public int MaxInstallmentsWithoutInterest { get; init; }
    public decimal InstallmentValue { get; init; }
}
```

Publishing two separate events for one underlying trigger (a price change) would mean two
EventBridge deliveries and two OpenSearch round trips to update the same document — worse
throughput for no benefit, since CatalogView is the only consumer of either concern. The publisher
computes the highlight fields using the active `GatewayCost` at the moment the price changes; if no
provider is configured yet, they are published as zero rather than skipping the event, so the
price itself still syncs.

CatalogView's existing `PriceSyncConsumer`/`PriceSyncHandler` merges **all four** fields into the
OpenSearch document in one call to `IProductSearchIndex.ApplyPricingAsync` — a single **partial**
`_update` with `doc_as_upsert` (never a full document replace, so it cannot clobber rating fields
owned by the rating consumers). `SearchDocument` gains `AvistaPrice`/`MaxInstallmentsWithoutInterest`/
`InstallmentValue`; the OpenSearch mapping (`EnsureIndexAsync`) gains the matching
`double`/`integer`/`double` fields, per ADR-0027's Future Constraints rule (any new
publicly-queryable `SearchDocument` field must be added to the mapping and the GraphQL `Product`
type in the same change — see §7). No separate consumer, Lambda, or EventBridge rule is introduced
for this — it is additional data on an event CatalogView already consumes.

### 5. The payment badge is CDC-only and price-change-triggered — a gateway-cost-only change goes stale

`gateway-costs` has no DynamoDB Stream, and a `setGatewayCost` call alone never publishes anything.
The payment badge on an existing product only refreshes on that product's next `PriceChangedEvent`
(i.e., the next `setNominalPrice` call), or via a manual `ProductBackfill` re-run (CatalogView's
existing one-time backfill worker, extended — see §6). This is a **deliberate, accepted trade-off**,
not a bug: it mirrors this repo's established CDC-only philosophy
([ADR-0012](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md),
[ADR-0025](./0025-payment-bounded-context-simulated-gateway-cdc.md)) — CatalogView never makes a
synchronous call to Pricing to keep a badge fresh. Gateway-cost changes are expected to be rare
(config-like), so staleness until the next price change (or an operator-triggered backfill) is an
acceptable cost for avoiding a fan-out mechanism that would otherwise need to touch every product.

### 6. `ProductBackfill` also computes payment highlights

CatalogView's `ProductBackfill` (used for the initial historical backfill and as the manual
re-sync path from §5) now also reads the active provider's row from `gateway-costs` (a single
`GetItem`, not a scan) and computes the same three fields for every product during backfill. The
calculation logic is a small, deliberately duplicated local copy of
`Pricing.Function`'s `InstallmentCalculator`/`GatewayCost` — the seeder does not take a project
reference to `Pricing.Function` for this, consistent with its existing pattern of duplicating
small decode logic for cross-context table reads (`ScanNominalPricesAsync`).

### 7. GraphQL: fixes the pre-existing `price` gap in the same change

`Product.price` was already computed and stored on `SearchDocument`/`GetProductResponse`/
`ProductSearchItem` (ADR-0027) but never exposed in `schema.graphql` — the JS resolvers
(`Query.product.js`/`Query.products.js`) explicitly dropped it. This ADR adds `price`,
`avistaPrice`, `maxInstallmentsWithoutInterest`, and `installmentValue` to the GraphQL `Product`
type and both resolvers in the same change, per ADR-0027's own Future Constraints rule.
`InstallmentPlan` gains `avistaPrice`. A new `GatewayCost` type and `setGatewayCost` mutation
(Admin-only, Lambda-backed) expose the write path; `installmentRates` uses the `AWSJSON` scalar
end-to-end (GraphQL argument → Lambda payload → DynamoDB Map), avoiding a separate list-of-pairs
shape.

---

## Applies To

- `src/Services/Pricing/Pricing.Function` (new `Modules/GatewayCosts`; rewritten
  `InstallmentOptions`/`InstallmentCalculator`/`GetInstallmentPlanHandler`; extended
  `PriceStreamPublisherFunction`), `Pricing.DevelopmentDataSeeder` (new `GatewayCostInitialData`
  seed)
- `src/Services/CatalogView/CatalogView.DevelopmentDataSeeder` (`ProductBackfill`'s deliberate local
  mirror of `InstallmentCalculator` — see §6 — updated to compute the same hybrid
  `Math.Max(marginBasedLimit, tierBasedLimit)` cap, including its own `ValueTiers` config parsing)
- `src/BuildingBlocks/BuildingBlocks.Messaging/Events` (`PriceChangedEvent`, extended with the
  three payment-highlight fields)
- `src/Services/CatalogView/CatalogView.Function` (`SearchDocument`, `IProductSearchIndex`/
  `OpenSearchProductIndex` — `ApplyPriceAsync` renamed `ApplyPricingAsync`, now merging all four
  fields — `PriceSyncHandler`, `GetProduct`/`SearchProducts` response DTOs),
  `CatalogView.DevelopmentDataSeeder` (`ProductBackfill`)
- `src/AppHost` (`PricingExtensions.cs`, `CatalogViewExtensions.cs`)
- `infra/constructs/pricing-dynamodb.ts`, `infra/constructs/pricing-lambdas.ts`,
  `infra/constructs/appsync-api.ts`, `infra/stacks/pricing-stack.ts`
- `src/WebApps/Shopping.Web.SPA.React` (`graphql/schema.graphql`, `graphql/resolvers/*.js`,
  `graphql/types.ts`, `app/api/graphql/local.ts`)

---

## Consequences

### Positive
- Installment plans reflect a real, per-provider fee table instead of a synthetic uniform rate —
  a materially closer approximation of how real payment processors price installments.
- À vista pricing exists for the first time, computed rather than hand-entered.
- The storefront can read price and payment highlights straight from the search document — no
  runtime calculation on the browsing path, which was the whole point of CatalogView's
  denormalization pattern.
- Fixes a real, pre-existing gap (`price` computed but never exposed in GraphQL).

### Negative / Costs
- A gateway-cost-only change does not refresh existing products' badges — an operator must either
  wait for the next price change per product or trigger a manual backfill. This is explicit and
  accepted (§5), but is a real operational step, not automatic.
- The à vista discount-passthrough model is a simplification; it does not represent a realistic
  merchant margin-retention policy.
- `ProductBackfill` now duplicates a small amount of calculation logic from `Pricing.Function`
  rather than sharing code — acceptable for a migration tool, but a maintenance seam if the
  calculation logic changes and the duplicate is forgotten.

### Mitigation Strategies
- Keep the `InstallmentCalculator` duplication in `ProductBackfill` small and clearly commented as
  a deliberate mirror, so a future change to the real calculator prompts an obvious manual update.
- If gateway-cost changes need to propagate faster than "next price change," a future iteration
  could add a batch job that re-publishes `PriceChangedEvent` for every product after a
  `setGatewayCost` call — deliberately not built here to avoid a fan-out mechanism for a rare,
  config-like operation.

---

## References
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First, Lambda for Complex Logic](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0012: Merge Discount into Basket — Coupon as an In-Process Entity](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md)
- [ADR-0025: Payment Bounded Context — Simulated Gateway via Fully Async EventBridge/CDC](./0025-payment-bounded-context-simulated-gateway-cdc.md)
- [ADR-0026: Pricing Bounded Context — Price and Campaign Ownership](./0026-pricing-bounded-context-price-and-campaign-ownership.md)
- [ADR-0027: CatalogView — OpenSearch Product Search and Rating Sync](./0027-catalogview-opensearch-product-search-and-rating-sync.md)
