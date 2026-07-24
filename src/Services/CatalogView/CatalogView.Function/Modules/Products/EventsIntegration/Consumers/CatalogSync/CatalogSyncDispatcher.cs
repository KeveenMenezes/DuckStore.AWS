namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync;

// Resolves the one ICatalogSyncStrategy that owns a given EventBridge detail-type and delegates
// to it (ADR-0040). Backs CatalogSyncConsumer only — which strategies are actually reachable is
// decided by which EventBridge rules target that Lambda, not by anything here.
public sealed class CatalogSyncDispatcher(IEnumerable<ICatalogSyncStrategy> strategies)
{
    public Task DispatchAsync(
        EventBridgeEvent<JsonElement> evt, CancellationToken cancellationToken = default)
    {
        var strategy = strategies.SingleOrDefault(s => s.CanHandle(evt.DetailType))
            ?? throw new InvalidOperationException(
                $"No Catalog sync strategy registered for detail-type '{evt.DetailType}'.");

        return strategy.HandleAsync(evt.Id, evt.Detail, cancellationToken);
    }
}
