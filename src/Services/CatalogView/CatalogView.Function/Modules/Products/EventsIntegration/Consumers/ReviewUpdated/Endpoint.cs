namespace CatalogView.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ReviewUpdateAggregateConsumer(
        EventBridgeEvent<ReviewUpdatedEvent> evt,
        [FromServices] ReviewUpdateAggregateHandler handler)
    {
        await handler.HandleAsync(evt.Id, evt.Detail);
    }
}
