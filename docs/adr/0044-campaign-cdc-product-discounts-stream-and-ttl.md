# ADR-0044: Campaign Changes Reach CatalogView via a `product-discounts` Stream and TTL

## Status
**Accepted** — July 2026. Implemented: `ProductDiscountStreamPublisherFunction` publishes
`ProductDiscountChangedEvent` off the `product-discounts` stream.

Amends [ADR-0040](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md) §1, whose
"`PriceChangedEvent` stays a plain 1:1 handler" rests on Pricing having a single occurrence. This ADR
adds Pricing's second, which is the condition ADR-0040's own Future Constraints name for regrouping.
It does not supersede [ADR-0026](./0026-pricing-bounded-context-price-and-campaign-ownership.md) §6 or
§7 — see Decision §1 and §3 for why both still hold.

---

## Context

Creating a campaign had no observable effect on the storefront. The admin creates a 10% campaign, and
the catalog card, the product listing and the cart all keep showing the undiscounted price —
indefinitely, not for a few seconds.

The cause is not in the campaign code. It is that `catalogview-products.Price` is **derived from two
independent inputs but subscribes to changes in only one**:

| Input | Owner | Change signal |
|---|---|---|
| Nominal price / cost | `prices` table | DynamoDB Stream → `PriceChangedEvent` |
| Active campaign discount | `product-discounts` table | *(none)* |

`DynamoCampaignRepository` writes `campaigns` and `product-discounts` and never touches `prices`, and
`product-discounts` had no stream. So no write ever woke the CDC path, and the denormalized price kept
whatever value the last *price* write left there. The schema already admitted this in a comment on
`Product.price` — *"a gateway-cost-only or campaign-only change goes stale until then"* — without
anything closing it.

The gap is wider than the reported symptom. Three occurrences change a product's effective price, and
none of them reached CatalogView:

- **A campaign starts.** The reported bug.
- **A campaign is ended** via `EndCampaign`. The catalog would keep showing the discounted price after
  the campaign was retracted — the symmetric defect, unreported.
- **A campaign expires** because `EndsAt` passed. Worst of the three: expiry is a read-time check with
  no scheduler (ADR-0026 §7), so *nothing writes anything ever again* and the catalog stays discounted
  permanently.

A rejected shortcut, recorded because it is the obvious one: have `CreateCampaign`/`EndCampaign` bump
`UpdatedAt` on each enrolled product's `prices` row so the existing stream fires. It reuses the whole
CDC path for two lines. It is also a lie in the data (`UpdatedAt` would no longer answer "when did the
price change?"), it makes the Campaigns module write to the Prices module's table, it uses CDC as an
RPC by manufacturing a fact-change to carry a notification, and it adds N items to a
`TransactWriteItems` already capped at 100 — halving the products a campaign can enrol.

---

## Decision

### 1. `product-discounts` gets a DynamoDB Stream and its own CDC publisher

A new Lambda, `pricing-product-discounts-stream-publisher`, is triggered by the table's stream and
publishes `ProductDiscountChangedEvent`. The trigger is the **committed write**, per
[ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md) — publishing inline
from `CreateCampaign`/`EndCampaign` is NOT ALLOWED.

All three record kinds converge on one recompute:

| Record | Cause | Published highlights |
|---|---|---|
| `INSERT` | `CreateCampaign` enrolled the product | discounted |
| `REMOVE` | `EndCampaign` retracted it, or TTL dropped it | undiscounted |
| `MODIFY` | overwritten by a newer campaign (ADR-0026 §8) | discounted, per the winner |

This does not contradict ADR-0026 §6. That section kept the **internal** fan-out
(`campaigns` → `product-discounts`) as a plain `TransactWriteItems`, correctly: it is a projection
within Pricing. A stream on `product-discounts` serves the **cross-service** notification, which is
what [ADR-0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
establishes Streams for. Different concern, same table.

The stream is `KEYS_ONLY`. The publisher takes only the product id from the record and **re-reads the
committed discount**, so the published figures reflect the state that actually won rather than the
image that happened to trigger the invocation. A `REMOVE` therefore needs no special case: there is
nothing left to read, so the recompute naturally yields the undiscounted price.

### 2. `ProductDiscountChangedEvent` is a distinct event, not a reused `PriceChangedEvent`

```csharp
public record ProductDiscountChangedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public decimal OriginalPrice { get; init; }
    public decimal Price { get; init; }
    public decimal CashPrice { get; init; }
    public int MaxInstallmentsWithoutInterest { get; init; }
    public decimal MaxInstallmentValue { get; init; }
}
```

It carries the same fields as `PriceChangedEvent`, and reusing that type would have cost nothing on
the consumer side. It is still rejected: the nominal price did not change.
[ADR-0031](./0031-cdc-events-named-after-domain-occurrence-no-changetype-discriminator.md) names an event after the
occurrence, not after what a consumer happens to do with it, and collapsing the two would make a
campaign indistinguishable from a merchant repricing on the bus — losing the ability to route, alarm
or attribute on the difference.

### 3. TTL on `product-discounts` closes natural expiry

Each row carries `ExpiresAt` (the campaign's `EndsAt`, unix seconds) and the table declares
`timeToLiveAttribute: 'ExpiresAt'`. DynamoDB's TTL delete **lands on the same stream as a `REMOVE`**,
so an expiring campaign rolls the catalog price back through the exact path an explicitly ended one
takes.

ADR-0026 §7 stands: vigência is still enforced at read time and remains authoritative. TTL is not a
scheduler flipping a status — it is a delete that happens to also be the CDC signal. Correctness never
depends on it firing, only the freshness of the denormalized copy does.

### 4. Pricing's CatalogView consumers regroup behind `IPricingSyncStrategy`

ADR-0040 §1 allowed `PriceChangedEvent` a plain 1:1 handler explicitly because Pricing produced one
occurrence, and its Future Constraints state what happens when that stops being true:

> *"...MAY stay a plain 1:1 handler (mirroring `PriceChangedEvent`) until/unless a second occurrence
> from that producer appears — at which point it gets its own
> `I<Producer>SyncStrategy`/`<Producer>SyncDispatcher` pair, not a branch bolted onto an existing
> producer's contract."*

So `Consumers/PriceChanged/` becomes `Consumers/PricingSync/` — dispatcher, `IPricingSyncStrategy`, and
one strategy per detail-type (`PriceChangedStrategy`, `ProductDiscountChangedStrategy`) — and
`catalogview-price-sync-consumer` becomes `catalogview-pricing-sync-consumer` with one EventBridge rule
per detail-type targeting it (ADR-0040 §4). This is mechanical compliance with an existing rule, not a
new decision.

### 5. One recompute, two triggers

`PricingHighlights` (Pricing, `Modules/Prices/EventsIntegration/Publishers`) owns "given a product,
compute the payment highlights from the active gateway cost and whatever discount is in force". Both
stream publishers MUST call it; neither may re-derive the calculation.

---

## Consequences

### Positive

- All three occurrences — campaign start, end, and expiry — now reach the catalog through one path,
  closing two defects that were never reported alongside the one that was.
- Rolling back needs no "campaign ended" notion anywhere: the publisher re-reads and the consumer
  applies absolute values, so every case is the same idempotent write.
- The `prices` table keeps meaning what it says; `UpdatedAt` still answers when the price changed.
- Campaign size is unaffected — the fan-out transaction is untouched.

### Negative / Costs

- **TTL is best-effort.** DynamoDB may delete an expired row hours after `EndsAt` (AWS documents
  typically within 48h). Until it does, the catalog card shows a discount that read-time checks
  already treat as gone — so the product page and cart are correct while the listing lags.
- One more Lambda, one more stream, one more event type, and a rename of a deployed function
  (`catalogview-price-sync-consumer` → `catalogview-pricing-sync-consumer`) that replaces rather than
  updates the existing resource.
- Enrolling a product in a campaign now costs one extra `GetItem` on `prices` plus a recompute per
  affected product, asynchronously.
- A campaign spanning N products emits N events. There is no batching; each is an independent
  projection write.

### Mitigation Strategies

- The authoritative read paths (`installmentPlanFor`, `basketInstallmentPlan`) check vigência at read
  time and never consult the denormalized copy, so TTL lag can only make a listing stale — never a
  checkout wrong.
- `ProductBackfill` (CatalogView's seeder) already recomputes highlights for every product and remains
  the manual escape hatch if a projection drifts.

### Future Constraints

- A third Pricing occurrence MUST become another `IPricingSyncStrategy`, not a branch inside an
  existing one (ADR-0040).
- A gateway-cost-only change still reaches no product — `gateway-costs` has no stream by design
  (ADR-0028). If that becomes user-visible, it needs its own decision, and the shape of this one is
  the precedent.

---

## Applies To

- `src/Services/Pricing/Pricing.Function` — `Modules/Prices/EventsIntegration/Publishers`,
  `Modules/Campaigns/Data/DynamoCampaignRepository`
- `src/Services/CatalogView/CatalogView.Function` — `Modules/Products/EventsIntegration/Consumers/PricingSync`
- `src/BuildingBlocks/BuildingBlocks.Messaging` — `Events/ProductDiscountChangedEvent`
- `infra/constructs/pricing-dynamodb.ts`, `infra/constructs/pricing-lambdas.ts`,
  `infra/constructs/catalogview-lambdas.ts`
- `src/AppHost/PricingExtensions.cs`, `src/AppHost/CatalogViewExtensions.cs`

## References

- [ADR-0005: Remove In-Process Domain Events — CDC via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
- [ADR-0026: Pricing Bounded Context — Price and Campaign Ownership](./0026-pricing-bounded-context-price-and-campaign-ownership.md) — §6 internal fan-out, §7 read-time vigência, §8 one campaign per product
- [ADR-0028: Gateway Cost Table and Payment Highlights](./0028-gateway-cost-table-and-payment-highlights.md)
- [ADR-0031: Thin Lifecycle Events Named After the Domain Occurrence](./0031-cdc-events-named-after-domain-occurrence-no-changetype-discriminator.md)
- [ADR-0040: CatalogView Consumers Consolidated by Producer via Strategy Dispatch](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md) — §1 and Future Constraints
- [ADR-0043: Cart Discount Allocation](./0043-cart-discount-allocation-policy.md)
