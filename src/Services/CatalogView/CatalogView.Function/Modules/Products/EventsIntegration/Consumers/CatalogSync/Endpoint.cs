namespace CatalogView.Function;

// Triggered by EventBridge. Groups every CatalogView consumer sourced from Catalog
// (ProductSyncedEvent, ProductDeletedEvent, CatalogCategorySyncEvent — ADR-0040) behind one
// Lambda, dispatching to the owning ISyncStrategy by detail-type.
public partial class Functions
{
    [LambdaFunction]
    public async Task CatalogSyncConsumer(
        EventBridgeEvent<JsonElement> evt,
        [FromServices] CatalogSyncDispatcher dispatcher)
    {
        await dispatcher.DispatchAsync(evt);
    }
}
