namespace CatalogView.Function;

// Triggered by EventBridge. Consumes CatalogCategorySyncEvent and rewrites the denormalized
// category name on every catalogview-products document that references it.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task CategorySyncConsumer(
        EventBridgeEvent<CatalogCategorySyncEvent> evt,
        [FromServices] CategorySyncHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
