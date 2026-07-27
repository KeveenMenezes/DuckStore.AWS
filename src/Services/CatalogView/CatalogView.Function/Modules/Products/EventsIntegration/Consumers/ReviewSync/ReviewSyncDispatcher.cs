namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync;

// Resolves the one IReviewSyncStrategy that owns a given EventBridge detail-type and delegates
// to it (ADR-0040). Backs ReviewSyncConsumer only — which strategies are actually reachable is
// decided by which EventBridge rules target that Lambda, not by anything here.
public sealed class ReviewSyncDispatcher(IEnumerable<IReviewSyncStrategy> strategies)
{
    public Task DispatchAsync(
        EventBridgeEvent<JsonElement> evt, CancellationToken cancellationToken = default)
    {
        var strategy = strategies.SingleOrDefault(s => s.CanHandle(evt.DetailType))
            ?? throw new InvalidOperationException(
                $"No Review sync strategy registered for detail-type '{evt.DetailType}'.");

        return strategy.HandleAsync(evt.Id, evt.Detail, cancellationToken);
    }
}
