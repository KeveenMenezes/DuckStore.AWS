namespace CatalogView.Function;

// Triggered by EventBridge. Groups every CatalogView consumer sourced from Pricing
// (PriceChangedEvent, ProductDiscountChangedEvent — ADR-0040/ADR-0044) behind one Lambda,
// dispatching to the owning IPricingSyncStrategy by detail-type.
public partial class Functions
{
    [LambdaFunction]
    public async Task PricingSyncConsumer(
        EventBridgeEvent<JsonElement> evt,
        [FromServices] PricingSyncDispatcher dispatcher)
    {
        await dispatcher.DispatchAsync(evt);
    }
}
