namespace CatalogView.Function;

// Triggered by EventBridge. Groups every CatalogView consumer sourced from Review
// (ReviewCreatedEvent, ReviewUpdatedEvent — ADR-0040) behind one Lambda, dispatching to the
// owning ISyncStrategy by detail-type.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ReviewSyncConsumer(
        EventBridgeEvent<JsonElement> evt,
        [FromServices] ReviewSyncDispatcher dispatcher)
    {
        await dispatcher.DispatchAsync(evt);
    }
}
