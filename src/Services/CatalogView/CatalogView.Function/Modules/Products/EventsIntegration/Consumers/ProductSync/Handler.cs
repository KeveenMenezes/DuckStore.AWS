using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ProductSync;

// Consumes CatalogProductSyncEvent (ADR-0027) and keeps the OpenSearch document in sync with
// Catalog's products table. Upsert/delete by id is naturally idempotent — no dedicated idempotency
// mechanism is needed here (unlike ApplyRatingAsync, which increments a counter).
public sealed class CatalogProductSyncHandler(IProductSearchIndex index)
{
    public Task HandleAsync(CatalogProductSyncEvent evt, CancellationToken cancellationToken = default) =>
        evt.ChangeType switch
        {
            "REMOVE" => index.DeleteAsync(evt.ProductId, cancellationToken),
            _ => index.UpsertAsync(ToDocument(evt), cancellationToken)
        };

    // Price is deliberately not set here: Pricing owns it (ADR-0026) and PriceSyncHandler merges
    // it separately — UpsertAsync's partial merge never touches the price field.
    private static SearchDocument ToDocument(CatalogProductSyncEvent evt) =>
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
