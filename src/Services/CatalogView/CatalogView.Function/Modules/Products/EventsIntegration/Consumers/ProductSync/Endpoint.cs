namespace CatalogView.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task CatalogProductSyncConsumer(
        EventBridgeEvent<CatalogProductSyncEvent> evt,
        [FromServices] CatalogProductSyncHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
