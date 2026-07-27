using BuildingBlocks.Messaging.Serialization;
namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync.Strategies;

// Consumes CatalogCategorySyncEvent (a category rename — ADR-0027 extension) and rewrites the
// denormalized category name on every product document that references it. Naturally idempotent —
// a redelivered rename is a no-op re-write, same reasoning as PriceSyncHandler.
public sealed class CategorySyncStrategy(IProductSearchIndex index) : ICatalogSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(CatalogCategorySyncEvent);

    public Task HandleAsync(
        string eventId, JsonElement detail, CancellationToken cancellationToken = default)
    {
        var evt = detail.Deserialize(MessagingSerializerContext.Default.CatalogCategorySyncEvent)!;
        return index.RenameCategoryAsync(evt.CategoryId, evt.Name, cancellationToken);
    }
}
