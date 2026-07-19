namespace CatalogView.Function;

// Triggered by EventBridge. Consumes ReviewUpdatedEvent and applies the rating delta to the
// catalogview-products projection.
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
