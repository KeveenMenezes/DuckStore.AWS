using Amazon.Lambda.DynamoDBEvents;
using Pricing.Function.Modules.Prices.EventsIntegration.Publishers;

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
    // document via ProductDeletedEvent in that case (ADR-0031).
    //
    // A campaign-only change no longer needs to wait for the next price write: it has its own
    // trigger now, ProductDiscountStreamPublisher (ADR-0044). A gateway-cost-only change still
    // does — the payment badge recomputes on the next price/campaign change or a manual
    // ProductBackfill re-run.
    [LambdaFunction]
    public async Task PriceStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] PricingHighlights pricingHighlights)
    {
        foreach (var record in dynamoEvent.Records)
        {
            if (record.EventName == "REMOVE")
                continue;

            var price = PriceStreamImage.From(record.Dynamodb.NewImage);
            if (price is null || !Guid.TryParse(price.ProductId, out var productId))
                continue;

            var breakdown = await pricingHighlights.ComputeAsync(
                productId, price.Cost, price.NominalPrice);

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
