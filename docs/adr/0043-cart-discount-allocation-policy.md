# ADR-0043: Cart Discount Allocation — Per-Product Campaigns Against a Single-Transaction Cart

## Status
**Accepted** — July 2026. Implemented: `GetBasketInstallmentPlanHandler` injects `ICampaignRepository`
and applies the allocation rule below.

This ADR does not supersede an existing record. It closes a gap left open between
[ADR-0026](./0026-pricing-bounded-context-price-and-campaign-ownership.md) (campaigns discount
individual products) and [ADR-0028](./0028-gateway-cost-table-and-payment-highlights.md) (a cart is
priced as one transaction): neither says what happens when the two meet.

---

## Context

Pricing exposes two installment queries built on the same `InstallmentCalculator`:

| Query | Scope | Campaign discount |
|---|---|---|
| `installmentPlanFor(productId)` | one product | applied — reads `product-discounts`, calls `CalculateWithOptionalDiscount` |
| `basketInstallmentPlan(items)` | whole cart | **ignored** |

`GetBasketInstallmentPlanHandler` did not inject `ICampaignRepository` at all. It called
`CalculateForCart` → `Calculate`, never `CalculateWithOptionalDiscount`. The discount was not lost
in the arithmetic — it was never read.

The concrete pains:

- **The same product quoted two different prices.** A product on campaign showed its discounted
  price on its own page and its undiscounted price in the cart total. Nothing reconciled them, and
  nothing detected the divergence.
- **The IAM grant matched the bug, not the intent.** `pricing-get-basket-installment-plan` was
  granted read on `prices` and `gateway-costs` only. Even with correct code the Lambda would have
  failed with `AccessDenied` on AWS — invisible locally, where Aspire grants unrestricted access to
  DynamoDB Local.
- **There was no rule to apply even if the discount had been read.** ADR-0028 §2 establishes that a
  cart is billed as one transaction (the gateway's flat fee is charged once per checkout, not once
  per item), so the cart has exactly one price. ADR-0026 §6 establishes that discounts attach to
  individual products. Reconciling a per-product concept with an aggregate price requires an
  allocation rule, and no ADR defined one.

That last point is the architectural gap. Allocation is a business policy — "how much of the cart's
one price is a given discounted product entitled to take off" — and the first implementation buried
it as unnamed arithmetic inside a private method of `InstallmentCalculator`, which is exactly the
placement [the thin-handlers-rich-domain rule](../../.claude/skills/thin-handlers-rich-domain/SKILL.md)
exists to prevent.

---

## Decision

### 1. The cart keeps one price; discounts reduce it, they do not re-price it

`CalculateForCart` MUST continue to sum `Cost`/`NominalPrice` across items and compute a single
`PricingBreakdown` from those totals, per ADR-0028 §2. Campaign discounts are applied as a
**reduction of that computed price**, never by summing per-item plans — summing per-item plans would
charge the gateway's flat fee once per line and break ADR-0028.

`TotalOriginalPrice` (the sticker/"De" price and the value-tier lookup's basket value) MUST remain
pre-discount, matching the rule `ApplyDiscount` already follows for a single product: a campaign
must never revoke an installment count the cart's real value already unlocked.

### 2. Allocation is proportional to nominal contribution, applied per unit

A discounted product's claim on the cart price is proportional to what it contributes to the cart's
nominal value; its discount is then applied **per unit** within that claim.

```csharp
// Modules/Campaigns/Domain/Services/CartDiscountAllocation.cs
var lineShare = cartPrice * (line.NominalValue / cartNominalValue);
var unitShare = lineShare / line.Quantity;

reduction += (unitShare - line.Discount.ApplyTo(unitShare)) * line.Quantity;
```

Two properties follow, both required:

- **A single unit of a single product reduces to exactly `ApplyDiscount`.** The line's claim is the
  whole cart price, so the cart total and the product page cannot disagree about the same discount.
  This is the defect this ADR closes, and it is enforced by an assertion against the single-product
  path, not by inspection:

  ```csharp
  var singleProduct = InstallmentCalculator.CalculateWithOptionalDiscount(/* same product */);
  Assert.Equal(singleProduct.Price, result.Price);
  ```

- **A fixed discount is taken once per unit, not once per line.** `R$5 off` on three units takes
  `R$15`, matching what the product page's per-unit price leads the customer to expect. An earlier
  draft applied it once per line; that was an artifact of `DiscountValue` carrying no quantity
  concept, not a decision, and is rejected here.

Percentage discounts are scale-invariant, so per-unit and per-line agree for them; the distinction
binds only on `DiscountType.Fixed`.

### 3. The policy lives in the domain, under a name

The rule MUST live in `Pricing.Function.Modules.Campaigns.Domain.Services.CartDiscountAllocation`
and MUST NOT be re-derived inside `InstallmentCalculator`, a handler, or a repository.
`InstallmentCalculator` MAY only apply the reduction the policy returns.

**Correct** — the calculator asks the policy and applies the answer:

```csharp
var reduction = CartDiscountAllocation.TotalReductionFrom(
    breakdown.Price, totalOriginalPrice, discountedLines);

var discountedPrice = Round(Math.Max(0, breakdown.Price - reduction));
```

**Incorrect** — the allocation rule inlined where it has no name and cannot be tested on its own:

```csharp
// ⛔ A business policy disguised as arithmetic inside a pricing calculation.
foreach (var (price, quantity, discount) in items.Where(i => i.Discount is not null))
{
    var share = breakdown.Price * (price.NominalPrice * quantity / totalOriginalPrice);
    totalDiscount += share - discount!.ApplyTo(share);
}
```

`CartDiscountAllocation` is a stateless domain service, not an interface with one implementation:
there is exactly one policy and no behavioural variance to dispatch on. Should a second allocation
basis ever be required (allocating by cost rather than nominal value, or discount stacking under
ADR-0026 §8), that is the point at which an abstraction is justified — not before.

This introduces `Domain/Services/` as a folder kind under `Modules/<Aggregate>/Domain/`, which no
module used previously (`Entities`, `ValueObjects`, `Enums`, `Dtos`). A stateless policy is neither
an entity nor a value object, and filing it under `ValueObjects/` would mislabel it.

### 4. Reads and grants

`GetBasketInstallmentPlanHandler` MUST resolve the active discount per **distinct** product id — the
same `GetActiveDiscountForProductAsync` read the single-product path performs — since a product may
appear on more than one basket line. There is no batch discount query, so the reads run in parallel.

`pricing-get-basket-installment-plan` MUST be granted read on `product-discounts` in
`infra/constructs/pricing-lambdas.ts`, alongside `prices` and `gateway-costs`.

---

## Consequences

### Positive

- The cart total and the product page can no longer disagree about the same discount, and a test
  asserts the equality directly against the single-product path rather than restating its expected
  output.
- The allocation rule has a name, a home in the domain, and its own unit tests covering the
  boundaries the handler tests never reached: zero/negative cart price, zero nominal value, a fixed
  amount exceeding a unit's own share, and full (100%) discounts across every line.
- Changing the policy — allocation basis, stacking, per-unit semantics — is now an edit to one
  named type, not surgery on a pricing calculation.

### Negative / Costs

- **Parity holds for one unit, not for quantities.** A cart of N units is not N times the
  product page's price, because the gateway's flat fee enters the cart once and the product page
  once. This divergence predates this ADR and is inherent to ADR-0028 §2; the allocation rule does
  not introduce it and does not remove it.
- **The allocation basis is a choice, not a derivation.** Proportional-to-nominal-value is
  defensible and produces the parity property above, but allocating by cost or by cash price would
  also have been defensible and would yield different splits on mixed-margin carts.
- **`DiscountValue.ApplyTo` is now called with an allocated slice of a card price**, while its
  parameter is named `nominalPrice`. The arithmetic is identical in shape, but the name no longer
  describes every caller.
- One more DynamoDB read per distinct cart product on a query that previously performed none.

### Mitigation Strategies

- The per-unit division (`lineShare / line.Quantity`) can lose precision on repeating decimals; the
  final `Round(..., 2, MidpointRounding.AwayFromZero)` on the cart price absorbs it, and
  `DiscountValue.ApplyTo` floors each unit at zero so the summed reduction can never exceed the
  cart price.
- Discount reads are issued once per **distinct** product and run concurrently, bounding the added
  latency at one round trip rather than one per basket line.

### Future Constraints

- Any new campaign targeting dimension (category, brand, customer segment) MUST express its
  reduction through `CartDiscountAllocation` rather than adding a second reduction path in the
  calculator.
- Lifting ADR-0026 §8's "one active campaign per product" assumption requires deciding stacking
  order **before** allocation runs — the policy assumes each line carries at most one discount.

---

## Applies To

- `src/Services/Pricing/Pricing.Function` — `Modules/Campaigns/Domain/Services`,
  `Modules/Prices/Features/GetBasketInstallmentPlan`, `Modules/Prices/Features/GetInstallmentPlan`
- `infra/constructs/pricing-lambdas.ts` — `pricing-get-basket-installment-plan` IAM grants

## References

- [ADR-0026: Pricing Bounded Context — Price and Campaign Ownership](./0026-pricing-bounded-context-price-and-campaign-ownership.md) — §6 campaign fan-out, §8 one active campaign per product
- [ADR-0028: Gateway Cost Table and Payment Highlights](./0028-gateway-cost-table-and-payment-highlights.md) — §2 the cart as a single billed transaction
- [ADR-0009: AppSync Resolver Selection](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) — why both installment queries are Lambda-backed
