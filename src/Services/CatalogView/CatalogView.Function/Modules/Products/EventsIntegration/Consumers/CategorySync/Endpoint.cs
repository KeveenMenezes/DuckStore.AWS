namespace CatalogView.Function;

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
