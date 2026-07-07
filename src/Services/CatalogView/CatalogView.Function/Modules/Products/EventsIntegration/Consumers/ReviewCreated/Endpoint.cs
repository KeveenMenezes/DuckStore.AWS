namespace CatalogView.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ReviewAggregateConsumer(
        EventBridgeEvent<ReviewCreatedEvent> evt,
        [FromServices] ReviewAggregateHandler handler)
    {
        await handler.HandleAsync(evt.Id, evt.Detail);
    }
}
