namespace Catalog.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ReviewCreatedConsumer(
        EventBridgeEvent<ReviewCreatedEvent> evt,
        [FromServices] ReviewCreatedHandler handler)
    {
        await handler.HandleAsync(evt.Id, evt.Detail);
    }
}
