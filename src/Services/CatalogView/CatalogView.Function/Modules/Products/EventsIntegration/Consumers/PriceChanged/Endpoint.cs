namespace CatalogView.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task PriceSyncConsumer(
        EventBridgeEvent<PriceChangedEvent> evt,
        [FromServices] PriceSyncHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
