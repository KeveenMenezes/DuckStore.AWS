using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;

namespace Pricing.Function.Modules.Prices.EventsIntegration.Publishers;

// The scalar payment highlights CatalogView denormalizes for catalog/card display, recomputed from
// whatever is in force right now: the active GatewayCost provider and the product's active campaign
// discount, if any.
//
// One operation, two triggers (ADR-0044): a write to `prices` (the merchant repriced) and a write
// to `product-discounts` (a campaign started, ended, or expired) both need exactly this, so it
// lives here rather than being duplicated in each stream publisher.
public sealed class PricingHighlights(
    IGatewayCostRepository gatewayCostRepository,
    ICampaignRepository campaignRepository,
    InstallmentOptions installmentOptions)
{
    public async Task<PricingBreakdown> ComputeAsync(
        Guid productId, decimal cost, decimal nominalPrice, CancellationToken cancellationToken = default)
    {
        var gatewayCost = await gatewayCostRepository.GetByProviderAsync(
            installmentOptions.ActiveProvider, cancellationToken);

        // No provider configured yet: publish the highlights as zero rather than skipping the event,
        // so the price itself still syncs to the search document.
        if (gatewayCost is null)
        {
            return new PricingBreakdown(0m, 0m, 0, 0m, []);
        }

        var breakdown = InstallmentCalculator.Calculate(
            cost, nominalPrice, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers);

        // Read at publish time, not taken from the stream image — on a product-discounts REMOVE
        // there is no discount left to read, which is exactly how an ended or expired campaign
        // publishes its undiscounted figures without any special-casing.
        var activeDiscount = await campaignRepository.GetActiveDiscountForProductAsync(
            productId, cancellationToken);

        if (activeDiscount is null)
        {
            return breakdown;
        }

        return InstallmentCalculator.ApplyDiscount(
            breakdown,
            nominalPrice,
            gatewayCost,
            DiscountValue.Of(activeDiscount.Type, activeDiscount.Amount),
            installmentOptions.ValueTiers);
    }
}
