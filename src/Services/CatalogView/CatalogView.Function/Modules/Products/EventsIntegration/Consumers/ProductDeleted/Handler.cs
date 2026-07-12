using CatalogView.Function.Modules.Products.Data;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ProductDeleted;

// Consumes ProductDeletedEvent (ADR-0031) — the same thin event Pricing consumes — and removes
// the product from the search index. Delete-by-id is naturally idempotent, so no dedicated
// idempotency mechanism is needed here.
public sealed class ProductDeletedHandler(IProductSearchIndex index)
{
    public Task HandleAsync(ProductDeletedEvent evt, CancellationToken cancellationToken = default) =>
        index.DeleteAsync(evt.ProductId, cancellationToken);
}
