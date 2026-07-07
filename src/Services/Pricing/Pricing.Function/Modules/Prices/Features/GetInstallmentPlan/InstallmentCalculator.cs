using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;

namespace Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;

public sealed record InstallmentPlanEntry(int Count, decimal Value, decimal TotalValue, bool HasInterest);

public sealed record PricingBreakdown(
    decimal Price,
    decimal CashPrice,
    int MaxInstallmentsWithoutInterest,
    decimal MaxInstallmentValue,
    IReadOnlyList<InstallmentPlanEntry> InstallmentPlan);

// Pure calculation, no I/O. Replaces the old flat "margin over sale price" walk with a cost-floor
// model: the merchant's minimum acceptable net (Cost + MinMarginPercent) drives both the à vista
// price and, backed out through the gateway's own 1x fee, the card price. From there,
// `originalPrice` (the sticker/"De" price) acts as a subsidy ceiling: as long as the price plus the
// card's percentage fee for a given installment count stays within that ceiling, the store absorbs
// the fee and the installment is interest-free to the customer; the first count that would blow the
// ceiling — and every count after it — passes the real fee on to the customer as interest.
public static class InstallmentCalculator
{
    public static PricingBreakdown Calculate(
        decimal cost, decimal originalPrice, GatewayCost gatewayCost, decimal minMarginPercent)
    {
        var floor = cost * (1 + minMarginPercent / 100m);

        var cashPrice = Round(floor + gatewayCost.FlatFeePerTransaction);

        var rate1 = gatewayCost.InstallmentRates[1];
        var price = Round((floor + gatewayCost.FlatFeePerTransaction) / (1 - rate1 / 100m));

        var (maxInstallmentsWithoutInterest, maxInstallmentValue, plan) =
            BuildInstallmentPlan(price, originalPrice, gatewayCost);

        return new PricingBreakdown(price, cashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    // Merges an active campaign's discount into the price side of the breakdown. originalPrice
    // (the ceiling) and cashPrice (cost-floor-derived, unrelated to the sticker markup) are
    // untouched — only the card price and the installment plan built on top of it are recomputed.
    public static PricingBreakdown ApplyDiscount(
        PricingBreakdown breakdown, decimal originalPrice, GatewayCost gatewayCost, DiscountValue discount)
    {
        var discountedPrice = Round(discount.ApplyTo(breakdown.Price));

        var (maxInstallmentsWithoutInterest, maxInstallmentValue, plan) =
            BuildInstallmentPlan(discountedPrice, originalPrice, gatewayCost);

        return new PricingBreakdown(
            discountedPrice, breakdown.CashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    // One-way latch: once an installment count blows the originalPrice ceiling, every count
    // beyond it also carries interest, even if a misconfigured rate table dips back down.
    // MaxInstallmentValue tracks in lockstep with MaxInstallmentsWithoutInterest (both only move on
    // the same !hasInterest branch) so it always equals the per-installment value at that exact
    // count — 1x's own price when no count clears the ceiling.
    private static (int MaxInstallmentsWithoutInterest, decimal MaxInstallmentValue, List<InstallmentPlanEntry> Plan)
        BuildInstallmentPlan(decimal price, decimal originalPrice, GatewayCost gatewayCost)
    {
        var maxInstallmentsWithoutInterest = 1;
        var maxInstallmentValue = price;
        var plan = new List<InstallmentPlanEntry>();
        var bufferExhausted = false;

        foreach (var count in gatewayCost.InstallmentRates.Keys.Where(k => k >= 2).OrderBy(k => k))
        {
            var rate = gatewayCost.InstallmentRates[count];
            var totalAtCount = price * (1 + rate / 100m);

            bool hasInterest;
            // Value is rounded first and TotalValue derived from it (not independently rounded)
            // so callers can always rely on TotalValue == Value * Count.
            var value = Round(totalAtCount / count);

            if (!bufferExhausted && totalAtCount <= originalPrice)
            {
                maxInstallmentsWithoutInterest = count;
                maxInstallmentValue = value;
                hasInterest = false;
            }
            else
            {
                bufferExhausted = true;
                hasInterest = true;
            }

            plan.Add(new InstallmentPlanEntry(count, value, value * count, hasInterest));
        }

        return (maxInstallmentsWithoutInterest, maxInstallmentValue, plan);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
