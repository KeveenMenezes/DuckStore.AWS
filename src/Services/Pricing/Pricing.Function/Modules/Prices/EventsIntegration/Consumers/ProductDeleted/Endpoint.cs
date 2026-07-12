namespace Pricing.Function;

// EventBridge-triggered consumer: reacts to CatalogUpdatedEvent (CDC from Catalog's products
// table) and deletes Pricing's own rows for a removed product — no synchronous call to Catalog.
// Idempotent via the pricing-processed-events inbox (evt.Id as key), same TransactWriteItems
// shape as Ordering's BasketCheckoutConsumer. Non-REMOVE change types are ignored.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task CatalogProductRemovedConsumer(
        EventBridgeEvent<CatalogUpdatedEvent> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        if (evt.Detail.ChangeType != "REMOVE")
            return;

        var productId = CatalogProductRemovedMapper.ToProductId(evt.Detail);
        var items = CatalogProductRemovedHandler.BuildCleanupTransactItems(productId);

        await consumer.ConsumeAsync(evt.Id, items);
    }
}
