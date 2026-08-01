using Pricing.Function.Modules.Campaigns.Domain.Services;
using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.Prices.Domain.Entities;
using Pricing.Function.Shared.Configuration;

namespace Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;

public sealed record InstallmentPlanEntry(int Count, decimal Value, decimal TotalValue, bool HasInterest);

public sealed record PricingBreakdown(
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    decimal MaxInstallmentValue,
    IReadOnlyList<InstallmentPlanEntry> InstallmentPlan);

public sealed record CartPricingBreakdown(decimal TotalOriginalPrice, PricingBreakdown Breakdown);

// Pure calculation, no I/O. Replaces the old flat "margin over sale price" walk with a cost-floor
// model: the merchant's minimum acceptable net (Cost + MinMarginPercent) drives both the à vista
// price and, backed out through the gateway's own 1x fee, the card price. From there,
// `originalPrice` (the sticker/"De" price) acts as a subsidy ceiling: as long as the price plus the
// card's percentage fee for a given installment count stays within that ceiling, the store absorbs
// the fee and the installment is interest-free to the customer; the first count that would blow the
// ceiling — and every count after it — passes the real fee on to the customer as interest.
//
// Hybrid cap (ADR-0028 §2): the final interest-free ceiling is the higher of two independent
// signals — marginBasedLimit (the ceiling walk above) and tierBasedLimit (originalPrice, which
// doubles as the caller's total basket value, checked against InstallmentOptions.ValueTiers). A
// large cart can unlock more installments than its per-unit margin alone would allow, but a tier
// can never take installments away from what margin already grants (Math.Max, never Math.Min).
public static class InstallmentCalculator
{
    public static PricingBreakdown Calculate(
        decimal cost, decimal originalPrice, GatewayCost gatewayCost, decimal minMarginPercent,
        IReadOnlyList<ValueTier> valueTiers)
    {
        var floor = cost * (1 + minMarginPercent / 100m);

        var cashPrice = Round(floor + gatewayCost.FlatFeePerTransaction);

        var rate1 = gatewayCost.InstallmentRates[1];
        var price = Round((floor + gatewayCost.FlatFeePerTransaction) / (1 - rate1 / 100m));

        var (maxInstallmentsWithoutInterest, maxInstallmentValue, plan) =
            BuildInstallmentPlan(price, originalPrice, gatewayCost, valueTiers);

        return new PricingBreakdown(price, cashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    // Treats the whole cart as one virtual transaction: sums Cost/NominalPrice across every item
    // (weighted by quantity) and runs the totals through Calculate. This is more correct than
    // summing already-computed per-item plans, since the gateway's flat fee is charged once per
    // checkout, not once per item — see ADR-0028. Each item may carry its own active campaign
    // discount, merged into the cart price by ApplyCartDiscounts below.
    public static CartPricingBreakdown CalculateForCart(
        IReadOnlyList<(Price Price, int Quantity, DiscountValue? Discount)> items, GatewayCost gatewayCost,
        decimal minMarginPercent, IReadOnlyList<ValueTier> valueTiers, decimal? customerDiscountAmount = null)
    {
        var totalCost = items.Sum(i => i.Price.Cost * i.Quantity);
        var totalOriginalPrice = items.Sum(i => i.Price.NominalPrice * i.Quantity);

        var breakdown = Calculate(totalCost, totalOriginalPrice, gatewayCost, minMarginPercent, valueTiers);
        var discounted = ApplyCartDiscounts(breakdown, items, totalOriginalPrice, gatewayCost, valueTiers);

        // Customer discount is cart-level and applied strictly after campaign allocation, on the
        // resulting total — never before, never per-line (ADR-0046 §5). Applying it earlier would
        // feed CartDiscountAllocation a reduced figure and break its single-unit-cart property.
        if (customerDiscountAmount is > 0)
        {
            discounted = ApplyCustomerDiscount(
                discounted, totalOriginalPrice, customerDiscountAmount.Value, gatewayCost, valueTiers);
        }

        return new CartPricingBreakdown(totalOriginalPrice, discounted);
    }

    // Subtracted from the cart's resulting price, floored at zero (ADR-0046 §5) — never allocated
    // per line, unlike the campaign reduction above. totalOriginalPrice stays the pre-discount
    // ceiling for the same reason ApplyCartDiscounts keeps it: neither discount may revoke an
    // installment count the cart's real value already unlocked.
    //
    // Unlike ApplyDiscount/ApplyCartDiscounts, this one *does* move cashPrice. A campaign discount
    // is a markdown on the sticker price, which cashPrice (cost-floor-derived) never tracked; a
    // customer discount is a fixed currency coupon the customer paid points for, and it has to be
    // worth the same however they pay. Leaving cashPrice alone would both hide the reward from
    // anyone paying à vista and let cashPrice exceed price.
    private static PricingBreakdown ApplyCustomerDiscount(
        PricingBreakdown breakdown, decimal totalOriginalPrice, decimal customerDiscountAmount,
        GatewayCost gatewayCost, IReadOnlyList<ValueTier> valueTiers)
    {
        var discountedPrice = Round(Math.Max(0, breakdown.Price - customerDiscountAmount));
        var discountedCashPrice = Round(Math.Max(0, breakdown.CashPrice - customerDiscountAmount));

        var (maxInstallmentsWithoutInterest, maxInstallmentValue, plan) =
            BuildInstallmentPlan(discountedPrice, totalOriginalPrice, gatewayCost, valueTiers);

        return new PricingBreakdown(
            discountedPrice, discountedCashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    // How much a discounted product may take off a cart billed as one transaction is a business
    // policy, not part of this calculation — CartDiscountAllocation owns it (ADR-0043). This method
    // only applies the resulting reduction and rebuilds the plan on top of it.
    private static PricingBreakdown ApplyCartDiscounts(
        PricingBreakdown breakdown,
        IReadOnlyList<(Price Price, int Quantity, DiscountValue? Discount)> items,
        decimal totalOriginalPrice,
        GatewayCost gatewayCost,
        IReadOnlyList<ValueTier> valueTiers)
    {
        var discountedLines = items
            .Where(i => i.Discount is not null)
            .Select(i => new DiscountedCartLine(i.Price.NominalPrice, i.Quantity, i.Discount!))
            .ToList();

        if (discountedLines.Count == 0)
        {
            return breakdown;
        }

        var reduction = CartDiscountAllocation.TotalReductionFrom(
            breakdown.Price, totalOriginalPrice, discountedLines);

        var discountedPrice = Round(Math.Max(0, breakdown.Price - reduction));

        // totalOriginalPrice stays pre-discount on purpose — same rule ApplyDiscount follows: a
        // campaign must never revoke an installment count the cart's real value already unlocked.
        var (maxInstallmentsWithoutInterest, maxInstallmentValue, plan) =
            BuildInstallmentPlan(discountedPrice, totalOriginalPrice, gatewayCost, valueTiers);

        return new PricingBreakdown(
            discountedPrice, breakdown.CashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    // Composes Calculate with an optional campaign discount as a single step, so callers never
    // have to branch on whether a discount is active — a null discount is simply a no-op.
    public static PricingBreakdown CalculateWithOptionalDiscount(
        decimal cost, decimal originalPrice, GatewayCost gatewayCost, decimal minMarginPercent,
        IReadOnlyList<ValueTier> valueTiers, DiscountValue? discount)
    {
        var breakdown = Calculate(cost, originalPrice, gatewayCost, minMarginPercent, valueTiers);

        return discount is null
            ? breakdown
            : ApplyDiscount(breakdown, originalPrice, gatewayCost, discount, valueTiers);
    }

    // Merges an active campaign's discount into the price side of the breakdown. originalPrice
    // (the ceiling, and the tier lookup's basket value) and cashPrice (cost-floor-derived, unrelated
    // to the sticker markup) are untouched — only the card price and the installment plan built on
    // top of it are recomputed. The tier lookup deliberately still uses the pre-discount
    // originalPrice: a campaign discount must never revoke an installment count the customer already
    // saw unlocked by the cart's real value.
    public static PricingBreakdown ApplyDiscount(
        PricingBreakdown breakdown, decimal originalPrice, GatewayCost gatewayCost, DiscountValue discount,
        IReadOnlyList<ValueTier> valueTiers)
    {
        var discountedPrice = Round(discount.ApplyTo(breakdown.Price));

        var (maxInstallmentsWithoutInterest, maxInstallmentValue, plan) =
            BuildInstallmentPlan(discountedPrice, originalPrice, gatewayCost, valueTiers);

        return new PricingBreakdown(
            discountedPrice, breakdown.CashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    // One-way latch: once an installment count blows the originalPrice ceiling, every count
    // beyond it also carries interest, even if a misconfigured rate table dips back down.
    private static int ComputeMarginBasedLimit(decimal price, decimal originalPrice, GatewayCost gatewayCost)
    {
        var limit = 1;
        var bufferExhausted = false;

        foreach (var count in gatewayCost.InstallmentRates.Keys.Where(k => k >= 2).OrderBy(k => k))
        {
            var totalAtCount = price * (1 + gatewayCost.InstallmentRates[count] / 100m);

            if (!bufferExhausted && totalAtCount <= originalPrice)
                limit = count;
            else
                bufferExhausted = true;
        }

        return limit;
    }

    // valueTiers is pre-sorted ascending by MinAmount (InstallmentOptions.FromConfiguration), so the
    // last tier whose MinAmount the basket clears is the highest-qualifying one.
    private static int ComputeTierBasedLimit(decimal totalBasketPrice, IReadOnlyList<ValueTier> valueTiers)
    {
        var limit = 0;

        foreach (var tier in valueTiers)
        {
            if (tier.MinAmount > totalBasketPrice)
                break;

            limit = tier.MaxInstallments;
        }

        return limit;
    }

    private static (int MaxInstallmentsWithoutInterest, decimal MaxInstallmentValue, List<InstallmentPlanEntry> Plan)
        BuildInstallmentPlan(
            decimal price, decimal originalPrice, GatewayCost gatewayCost, IReadOnlyList<ValueTier> valueTiers)
    {
        var marginBasedLimit = ComputeMarginBasedLimit(price, originalPrice, gatewayCost);
        var tierBasedLimit = ComputeTierBasedLimit(originalPrice, valueTiers);

        var highestAvailableCount = gatewayCost.InstallmentRates.Keys.Any()
            ? gatewayCost.InstallmentRates.Keys.Max()
            : 1;
        var finalLimit = Math.Min(Math.Max(marginBasedLimit, tierBasedLimit), highestAvailableCount);

        var maxInstallmentValue = price;
        var plan = new List<InstallmentPlanEntry>();

        foreach (var count in gatewayCost.InstallmentRates.Keys.Where(k => k >= 2).OrderBy(k => k))
        {
            var rate = gatewayCost.InstallmentRates[count];
            var totalAtCount = price * (1 + rate / 100m);

            // Value is rounded first and TotalValue derived from it (not independently rounded)
            // so callers can always rely on TotalValue == Value * Count.
            var value = Round(totalAtCount / count);
            var hasInterest = count > finalLimit;

            if (!hasInterest)
                maxInstallmentValue = value;

            plan.Add(new InstallmentPlanEntry(count, value, value * count, hasInterest));
        }

        return (finalLimit, maxInstallmentValue, plan);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
