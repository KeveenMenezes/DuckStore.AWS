namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync.Strategies;

// Consumes ProductDeletedEvent (ADR-0031) — the same thin event Pricing consumes — and removes
// the product from the search index. Delete-by-id is naturally idempotent, so no dedicated
// idempotency mechanism is needed here.
public sealed class ProductDeleteStrategy(IProductSearchIndex index) : ICatalogSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ProductDeletedEvent);

    public Task HandleAsync(
        string eventId, JsonElement detail, CancellationToken cancellationToken = default) =>
        index.DeleteAsync(detail.Deserialize<ProductDeletedEvent>()!.ProductId, cancellationToken);
}
