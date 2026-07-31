namespace Pricing.Function.Modules.Campaigns.Domain.Services;

// One discounted line of a cart: the product's unit sticker price, how many units are in the cart,
// and the campaign discount active on that product.
public sealed record DiscountedCartLine(decimal UnitNominalPrice, int Quantity, DiscountValue Discount)
{
    public decimal NominalValue => UnitNominalPrice * Quantity;
}

// A cart is billed as a single transaction (ADR-0028: the gateway's flat fee is charged once per
// checkout, not once per item), but campaigns discount individual products. Something therefore has
// to decide how much of the cart's one price each discounted product is entitled to take off — and
// that is a business policy, not arithmetic, so it lives here under a name instead of being implied
// by the order of statements inside InstallmentCalculator (ADR-0043).
//
// The policy, in one sentence: a product's claim on the cart price is proportional to what it
// contributes to the cart's nominal value, and its discount is applied per unit within that claim.
//
// Two properties fall out of it, both deliberate:
//   * A cart holding a single unit of one product reduces to exactly what ApplyDiscount computes for
//     that product on its own — the cart total and the product page can never disagree about the
//     same discount, which is the defect ADR-0043 was written to close.
//   * A fixed "R$5 off" is taken once per unit, so three discounted units take R$15 off — matching
//     what the product page's per-unit price leads the customer to expect.
public static class CartDiscountAllocation
{
    // Returns how much comes off the cart's price. Never exceeds it: DiscountValue.ApplyTo floors
    // each unit at zero, so the per-line reductions can at most add up to the lines' own shares.
    public static decimal TotalReductionFrom(
        decimal cartPrice, decimal cartNominalValue, IReadOnlyList<DiscountedCartLine> lines)
    {
        if (cartPrice <= 0 || cartNominalValue <= 0 || lines.Count == 0)
        {
            return 0m;
        }

        var reduction = 0m;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                continue;
            }

            var lineShare = cartPrice * (line.NominalValue / cartNominalValue);
            var unitShare = lineShare / line.Quantity;

            reduction += (unitShare - line.Discount.ApplyTo(unitShare)) * line.Quantity;
        }

        return reduction;
    }
}
