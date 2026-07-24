namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync.Strategies;

// Consumes ProductSyncedEvent (ADR-0027/ADR-0031) and upserts the catalogview-products item to
// match Catalog's products table (ADR-0030). Deletes are handled separately by
// ProductDeleteStrategy, since ProductSyncedEvent only fires on create/update. Upsert by id is
// naturally idempotent — no dedicated idempotency mechanism is needed here.
public sealed class ProductSyncStrategy(IProductSearchIndex index) : ICatalogSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ProductSyncedEvent);

    public Task HandleAsync(
        string eventId, JsonElement detail, CancellationToken cancellationToken = default) =>
        index.UpsertAsync(ToDocument(detail.Deserialize<ProductSyncedEvent>()!), cancellationToken);

    // Price is deliberately not set here: Pricing owns it (ADR-0026) and PriceSyncHandler merges
    // it separately — UpsertAsync's partial merge never touches the price field.
    private static SearchDocument ToDocument(ProductSyncedEvent evt) =>
        new()
        {
            Id = evt.ProductId,
            Name = evt.Name,
            Description = evt.Description,
            Images = [.. evt.Images.Select(i => new ImageRef(i.ImageId, i.IsMain, i.Order))],
            Stock = evt.Stock,
            CategoryIds = evt.CategoryIds,
            Categories = [.. evt.CategoryIds.Zip(evt.CategoryNames, (id, name) => new CategoryRef(id, name))]
        };
}
