using Amazon.Lambda.DynamoDBEvents;
using Pricing.Function.Modules.Prices.EventsIntegration.Publishers;
using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;

namespace Pricing.Function;

public partial class Functions
{
    // Triggered by the prices DynamoDB Stream. For each INSERT/MODIFY it publishes a single
    // PriceChangedEvent carrying the sticker price, the cost-derived card/cash prices, and the
    // max interest-free installment count — the scalar highlights CatalogView denormalizes for
    // catalog/card display — computed from the currently active GatewayCost provider plus any
    // active campaign discount (ADR-0005/0026/0027/0028). The detailed per-installment plan
    // is NOT published: it's computed synchronously by GetInstallmentPlan when the product detail
    // page needs it, so it never lands in the search document. One event per trigger, not one event per
    // concern, so CatalogView applies everything in a single merge. REMOVE is skipped: a price row
    // only disappears when the product itself is removed, and CatalogView deletes the whole
    // document via CatalogProductSyncEvent in that case.
    //
    // A gateway-cost-only or campaign-only change never reaches here — the payment badge only
    // recomputes on the next price change or a manual ProductBackfill re-run (CDC-only philosophy,
    // ADR-0012/0025). If no provider is configured yet, the highlight fields are published as zero
    // rather than skipping the event, so the price itself still syncs.
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task PriceStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] IGatewayCostRepository gatewayCostRepository,
        [FromServices] ICampaignRepository campaignRepository,
        [FromServices] InstallmentOptions installmentOptions)
    {
        foreach (var record in dynamoEvent.Records)
        {
            if (record.EventName == "REMOVE")
                continue;

            var price = PriceStreamImage.From(record.Dynamodb.NewImage);
            if (price is null || string.IsNullOrEmpty(price.ProductId))
                continue;

            var gatewayCost = await gatewayCostRepository.GetByProviderAsync(installmentOptions.ActiveProvider);

            var breakdown = new PricingBreakdown(0m, 0m, 0, 0m, []);

            if (gatewayCost is not null)
            {
                breakdown = InstallmentCalculator.Calculate(
                    price.Cost, price.NominalPrice, gatewayCost, installmentOptions.MinMarginPercent,
                    installmentOptions.ValueTiers);

                var activeDiscount = await campaignRepository.GetActiveDiscountForProductAsync(
                    Guid.Parse(price.ProductId));

                if (activeDiscount is not null)
                {
                    var discount = DiscountValue.Of(activeDiscount.Type, activeDiscount.Amount);
                    breakdown = InstallmentCalculator.ApplyDiscount(
                        breakdown, price.NominalPrice, gatewayCost, discount, installmentOptions.ValueTiers);
                }
            }

            await eventPublisher.PublishAsync(new PriceChangedEvent
            {
                ProductId = price.ProductId,
                OriginalPrice = price.NominalPrice,
                Price = breakdown.Price,
                CashPrice = breakdown.CashPrice,
                MaxInstallmentsWithoutInterest = breakdown.MaxInstallmentsWithoutInterest,
                MaxInstallmentValue = breakdown.MaxInstallmentValue
            });
        }
    }
}
