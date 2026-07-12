using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ProductSync;

// Consumes ProductSyncedEvent (ADR-0027/ADR-0031) and upserts the catalogview-products item to
// match Catalog's products table (ADR-0030). Deletes are handled separately by
// ProductDeletedHandler, since ProductSyncedEvent only fires on create/update. Upsert by id is
// naturally idempotent — no dedicated idempotency mechanism is needed here.
public sealed class ProductSyncedHandler(IProductSearchIndex index)
{
    public Task HandleAsync(ProductSyncedEvent evt, CancellationToken cancellationToken = default) =>
        index.UpsertAsync(ToDocument(evt), cancellationToken);

    // Price is deliberately not set here: Pricing owns it (ADR-0026) and PriceSyncHandler merges
    // it separately — UpsertAsync's partial merge never touches the price field.
    private static SearchDocument ToDocument(ProductSyncedEvent evt) =>
        new()
        {
            Id = evt.ProductId,
            Name = evt.Name,
            Description = evt.Description,
            ImageUrl = evt.ImageUrl,
            Stock = evt.Stock,
            CategoryIds = evt.CategoryIds,
            Categories = [.. evt.CategoryIds.Zip(evt.CategoryNames, (id, name) => new CategoryRef(id, name))]
        };
}
