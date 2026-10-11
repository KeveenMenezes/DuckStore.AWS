# Pricing

Owns price and promotion for the whole system: nominal price, promotional campaigns, payment-gateway
cost, and what the customer's points are worth in the cart. The largest context by resource count —
three modules and five tables.

## Architecture

![Pricing architecture](../../../docs/diagrams/pricing.svg)

<sub>Source: [`docs/duckstore-process-flow.drawio`](../../../docs/duckstore-process-flow.drawio), page **Pricing**. Regenerate with `./scripts/export-diagrams.sh`.</sub>

## Responsibilities

| Module | Owns |
|---|---|
| **Prices** | The nominal price; publishes `PriceChangedEvent` by CDC |
| **Campaigns** | Promotional campaigns; create/end writes `campaigns` + `product-discounts` in one `TransactWriteItems` |
| **GatewayCosts** | Per-provider cost, feeding the installment calculation ([ADR-0028](../../../docs/adr/0028-gateway-cost-table-and-payment-highlights.md)) |

**Points in the cart** ([ADR-0048 §3](../../../docs/adr/0048-points-transactions-ledger-and-in-cart-redemption.md)).
`GetBasketInstallmentPlan` takes `PointsToUse`: 100 points = R$1, capped at 20% of the cart's
post-campaign price, applied as a cart-level discount after the campaign allocation, and the cash
price and every installment are recomputed over the net total. Above the cap the quote is rejected
(`PointsAboveCap`), never trimmed. Pricing never sees the customer's balance — Challenges reserves
the points after checkout — and Challenges never sees money.

**The total charged is Pricing's** ([ADR-0048 §4](../../../docs/adr/0048-points-transactions-ledger-and-in-cart-redemption.md)).
The same query is the `quote` step of the `checkoutBasket` AppSync pipeline: with `ExpectedTotal`,
`PaymentMethod` and `Installments` it picks the total for that payment choice and rejects a client
total that differs by even a cent (`PriceChanged`, with `ServerTotal`). Both rejections come back as
data (`Rejection`) so the resolver can raise a typed GraphQL error.

**Basket never calls Pricing** — neither synchronously nor asynchronously
([ADR-0026](../../../docs/adr/0026-pricing-bounded-context-price-and-campaign-ownership.md), which
superseded ADR-0012 and moved discount ownership out of Basket entirely). The checkout pipeline
calls Pricing from AppSync, before Basket.

The old reward coupon (`CustomerDiscounts`, [ADR-0046](../../../docs/adr/0046-challenge-points-redeem-into-pricing-customer-discount.md))
was removed when in-cart points replaced it.

## Data

| Table | Key | Stream | TTL |
|---|---|---|---|
| `prices` | PK `ProductId` | `NEW_IMAGE` | — |
| `campaigns` | PK `Id` | — | — |
| `product-discounts` | PK `ProductId` | `KEYS_ONLY` | `ExpiresAt` |
| `gateway-costs` | PK `Provider` | — | — |
| `pricing-processed-events` | PK `PK` | — | — |

### Why `product-discounts` has a stream

A campaign write never touches `prices`. Without this stream nothing would wake the CDC path, and
CatalogView would keep the pre-campaign price forever. TTL on `ExpiresAt` (the campaign's `EndsAt`)
turns a natural expiry into a REMOVE record on that same stream, so an expired campaign rolls the
catalog price back with no scheduler
([ADR-0044](../../../docs/adr/0044-campaign-cdc-product-discounts-stream-and-ttl.md)).

Expiry is checked **at read time** and is authoritative; TTL only sweeps dead rows eventually.

## API surface (AppSync)

| Field | Resolver |
|---|---|
| `Query.nominalPriceFor` · `Mutation.setNominalPrice` | Direct DynamoDB |
| `Query.campaigns` | Direct DynamoDB (Scan, `Admin` group only) |
| `Mutation.setGatewayCost` | Direct DynamoDB |
| `Query.rewardConversion` | **NONE (local) resolver** — values baked in at synth time from `reward-config.ts`, no backend call |
| `Query.installmentPlanFor` · `basketInstallmentPlan` | Lambda ([ADR-0009](../../../docs/adr/0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) escalation); `pointsToUse > 0` is Cognito-only |
| `Mutation.checkoutBasket` (step `quote`) | Lambda — `pricing-get-basket-installment-plan` in the checkout pipeline |
| `Mutation.createCampaign` · `endCampaign` | Lambda — transactional fan-out |

## Integration events

**Consumes**

| Rule | Event | Effect |
|---|---|---|
| `pricing-product-deleted-consumer-rule` | `ProductDeletedEvent` | Cleans up the deleted product's price and discount |

**Publishes**

- `PriceChangedEvent` — `pricing-prices-stream-publisher`
- `ProductDiscountChangedEvent` — `pricing-product-discounts-stream-publisher`

Both are consumed by `catalogview-pricing-sync-consumer`.

## Failure handling

`pricing-dlq` collects all three asynchronous paths — the consumer (`onFailure` + the rule target's
`deadLetterQueue`) and two stream publishers (after bisect + 3 retries). Non-empty →
`pricing-dlq-not-empty` → `duckstore-alerts`.

## Local development

```bash
dotnet run --project src/AppHost/AppHost.csproj
dotnet test tests/Services/Pricing/Pricing.UnitTests/Pricing.UnitTests.csproj
```

## Related ADRs

[ADR-0026](../../../docs/adr/0026-pricing-bounded-context-price-and-campaign-ownership.md) ·
[ADR-0028](../../../docs/adr/0028-gateway-cost-table-and-payment-highlights.md) ·
[ADR-0043](../../../docs/adr/0043-cart-discount-allocation-policy.md) ·
[ADR-0044](../../../docs/adr/0044-campaign-cdc-product-discounts-stream-and-ttl.md) ·
[ADR-0046](../../../docs/adr/0046-challenge-points-redeem-into-pricing-customer-discount.md) ·
[ADR-0048](../../../docs/adr/0048-points-transactions-ledger-and-in-cart-redemption.md)
