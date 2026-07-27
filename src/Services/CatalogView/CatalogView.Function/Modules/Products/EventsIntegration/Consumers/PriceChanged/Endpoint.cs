namespace CatalogView.Function;

// Triggered by EventBridge. Consumes PriceChangedEvent and merges the price/payment fields into
// the catalogview-products projection.
public partial class Functions
{
    [LambdaFunction]
    public async Task PriceSyncConsumer(
        EventBridgeEvent<PriceChangedEvent> evt,
        [FromServices] PriceSyncHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
