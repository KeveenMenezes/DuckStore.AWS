namespace CatalogView.Function;

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
