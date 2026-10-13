using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.InventorySync;

namespace CatalogView.Function;

// Triggered by EventBridge (ProductStockChangedEvent). Inventory has a single event, so this is a 1:1
// consumer without a dispatcher (ADR-0040, ADR-0052 §8).
public partial class Functions
{
    [LambdaFunction]
    public async Task InventorySyncConsumer(
        EventBridgeEvent<ProductStockChangedEvent> evt,
        [FromServices] InventorySyncHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
