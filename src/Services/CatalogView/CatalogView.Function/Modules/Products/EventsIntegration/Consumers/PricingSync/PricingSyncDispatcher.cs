namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PricingSync;

// Resolves the one IPricingSyncStrategy that owns a given EventBridge detail-type and delegates to
// it (ADR-0040). Backs PricingSyncConsumer only — which strategies are actually reachable is
// decided by which EventBridge rules target that Lambda, not by anything here.
public sealed class PricingSyncDispatcher(IEnumerable<IPricingSyncStrategy> strategies)
{
    public Task DispatchAsync(
        EventBridgeEvent<JsonElement> evt, CancellationToken cancellationToken = default)
    {
        var strategy = strategies.SingleOrDefault(s => s.CanHandle(evt.DetailType))
            ?? throw new InvalidOperationException(
                $"No Pricing sync strategy registered for detail-type '{evt.DetailType}'.");

        return strategy.HandleAsync(evt.Id, evt.Detail, cancellationToken);
    }
}
