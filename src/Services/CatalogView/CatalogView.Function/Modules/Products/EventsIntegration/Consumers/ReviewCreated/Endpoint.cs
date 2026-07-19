namespace CatalogView.Function;

// Triggered by EventBridge. Consumes ReviewCreatedEvent and folds the new rating into the
// catalogview-products projection.
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
