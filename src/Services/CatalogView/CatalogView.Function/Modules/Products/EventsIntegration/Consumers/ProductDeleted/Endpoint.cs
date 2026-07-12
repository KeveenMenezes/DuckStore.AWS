namespace CatalogView.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ProductDeletedConsumer(
        EventBridgeEvent<ProductDeletedEvent> evt,
        [FromServices] ProductDeletedHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
