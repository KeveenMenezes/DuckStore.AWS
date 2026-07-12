namespace Pricing.Function;

// EventBridge-triggered consumer: reacts to ProductDeletedEvent (CDC from Catalog's products
// table, ADR-0031) and deletes Pricing's own rows for the removed product — no synchronous call
// to Catalog. Idempotent via the pricing-processed-events inbox (evt.Id as key), same
// TransactWriteItems shape as Ordering's BasketCheckoutConsumer.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ProductDeletedConsumer(
        EventBridgeEvent<ProductDeletedEvent> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var productId = ProductDeletedMapper.ToProductId(evt.Detail);
        var items = ProductDeletedHandler.BuildCleanupTransactItems(productId);

        await consumer.ConsumeAsync(evt.Id, items);
    }
}
