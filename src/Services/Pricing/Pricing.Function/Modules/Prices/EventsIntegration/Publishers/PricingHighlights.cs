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
    // Registered Scoped and the Lambda runtime opens one scope per invocation, so this memoizes the
    // active provider's cost for exactly one Streams batch — the lifetime where re-reading it is
    // pure waste (batchSize is 10, the item is the same one every time, and it changes far more
    // rarely than a batch is long). A new invocation gets a fresh instance and re-reads it, so a
    // gateway-cost edit is still picked up on the next batch.
    private GatewayCost? _gatewayCost;
    private bool _gatewayCostLoaded;

    public async Task<PricingBreakdown> ComputeAsync(
        Guid productId, decimal cost, decimal nominalPrice, CancellationToken cancellationToken = default)
    {
        // Independent reads: the product's discount doesn't depend on the gateway cost, so they
        // overlap rather than stacking two round trips onto every record in the batch.
        var gatewayCostTask = GetGatewayCostAsync(cancellationToken);
        // Read at publish time, not taken from the stream image — on a product-discounts REMOVE
        // there is no discount left to read, which is exactly how an ended or expired campaign
        // publishes its undiscounted figures without any special-casing.
        var activeDiscountTask = campaignRepository.GetActiveDiscountForProductAsync(
            productId, cancellationToken);

        await Task.WhenAll(gatewayCostTask, activeDiscountTask);

        var gatewayCost = await gatewayCostTask;

        // No provider configured yet: publish the highlights as zero rather than skipping the event,
        // so the price itself still syncs to the search document.
        if (gatewayCost is null)
        {
            return new PricingBreakdown(0m, 0m, 0, 0m, []);
        }

        var breakdown = InstallmentCalculator.Calculate(
            cost, nominalPrice, gatewayCost, installmentOptions.MinMarginPercent, installmentOptions.ValueTiers);

        var activeDiscount = await activeDiscountTask;

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

    // A separate flag rather than a null check: "no provider configured" is a legitimate answer
    // (see ComputeAsync), and caching it matters as much as caching a hit — otherwise the miss
    // path re-reads on every record of the batch.
    private async Task<GatewayCost?> GetGatewayCostAsync(CancellationToken cancellationToken)
    {
        if (_gatewayCostLoaded)
        {
            return _gatewayCost;
        }

        _gatewayCost = await gatewayCostRepository.GetByProviderAsync(
            installmentOptions.ActiveProvider, cancellationToken);
        _gatewayCostLoaded = true;

        return _gatewayCost;
    }
}
