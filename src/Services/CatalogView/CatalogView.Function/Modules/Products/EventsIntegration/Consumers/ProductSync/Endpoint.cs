namespace CatalogView.Function;

// Triggered by EventBridge. Consumes ProductSyncedEvent and upserts the catalogview-products
// projection.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task CatalogProductSyncConsumer(
        EventBridgeEvent<ProductSyncedEvent> evt,
        [FromServices] ProductSyncedHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
