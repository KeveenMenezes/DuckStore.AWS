namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CategorySync;

// Consumes CatalogCategorySyncEvent (a category rename — ADR-0027 extension) and rewrites the
// denormalized category name on every product document that references it. Naturally idempotent —
// a redelivered rename is a no-op re-write, same reasoning as PriceSyncHandler.
public sealed class CategorySyncHandler(IProductSearchIndex index)
{
    public Task HandleAsync(CatalogCategorySyncEvent evt, CancellationToken cancellationToken = default) =>
        index.RenameCategoryAsync(evt.CategoryId, evt.Name, cancellationToken);
}
