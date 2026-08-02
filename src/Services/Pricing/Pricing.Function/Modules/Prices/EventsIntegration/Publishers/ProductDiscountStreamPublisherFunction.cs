using Amazon.Lambda.DynamoDBEvents;

namespace Pricing.Function;

public partial class Functions
{
    // Triggered by the product-discounts DynamoDB Stream — the trigger that was missing (ADR-0044).
    // A campaign write never touches `prices`, so before this Lambda existed nothing woke the CDC
    // path and CatalogView's denormalized price stayed at its pre-campaign value indefinitely.
    //
    // Every record kind matters, and all three converge on the same recompute:
    //   INSERT — CreateCampaign enrolled the product   -> publishes the discounted highlights
    //   REMOVE — EndCampaign retracted the row, or DynamoDB TTL dropped it once EndsAt passed
    //            -> no discount left to read, so it publishes the undiscounted highlights
    //   MODIFY — the row was overwritten by a newer campaign (ADR-0026 §8) -> republished
    //
    // The stream is KEYS_ONLY on purpose: the product id is all this needs from the record, and the
    // discount itself is re-read from the committed table so the published figures reflect the
    // state that actually won, not the image that happened to trigger us.
    [LambdaFunction]
    public async Task ProductDiscountStreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] IPriceRepository priceRepository,
        [FromServices] PricingHighlights pricingHighlights)
    {
        // Collected, then published in one batched call at the end — PutEvents takes 10 entries and
        // this trigger's batchSize is 10, so a full batch costs one API call instead of ten.
        var instructions = new List<PublishInstruction>();

        foreach (var record in dynamoEvent.Records)
        {
            if (record.Dynamodb?.Keys is not { } keys ||
                !keys.TryGetValue("ProductId", out var key) ||
                !Guid.TryParse(key.S, out var productId))
            {
                continue;
            }

            // Gone from `prices` means the product itself was deleted; CatalogView removes the whole
            // document off ProductDeletedEvent (ADR-0031), so there is nothing to refresh here.
            var price = await priceRepository.GetByProductIdAsync(productId);
            if (price is null)
                continue;

            var breakdown = await pricingHighlights.ComputeAsync(productId, price.Cost, price.NominalPrice);

            instructions.Add(new PublishInstruction(
                nameof(ProductDiscountChangedEvent),
                new ProductDiscountChangedEvent
                {
                    ProductId = productId.ToString(),
                    OriginalPrice = price.NominalPrice,
                    Price = breakdown.Price,
                    CashPrice = breakdown.CashPrice,
                    MaxInstallmentsWithoutInterest = breakdown.MaxInstallmentsWithoutInterest,
                    MaxInstallmentValue = breakdown.MaxInstallmentValue
                }));
        }

        if (instructions.Count > 0)
            await eventPublisher.PublishManyAsync(instructions);
    }
}
