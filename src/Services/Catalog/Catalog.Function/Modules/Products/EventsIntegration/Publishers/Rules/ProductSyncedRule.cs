namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Fires on product INSERT/MODIFY and produces a ProductSyncedEvent carrying the full product
// payload, so CatalogView can upsert its search document without a synchronous call back into
// Catalog (ADR-0027). Deletes are handled separately by ProductDeletedRule, which carries no
// payload (ADR-0031). Runs alongside ProductCreatedRule/ProductUpdatedRule in the same
// StreamRuleDispatcher<CatalogStreamImage> — one Streams record can fan out to more than one
// integration event.
public sealed class ProductSyncedRule(ICategoryRepository categoryRepository) : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        context.EventName is "INSERT" or "MODIFY" && !string.IsNullOrEmpty(context.New?.Id);

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogStreamImage> context, CancellationToken cancellationToken = default)
    {
        var image = context.New!;

        // Resolve category names here (CDC-only — no synchronous call back into Catalog from
        // CatalogView) so the product's search document can display names without CatalogView
        // needing to know anything about the categories table (ADR-0027 extension).
        var categoryIds = image.CategoryIds.Select(Guid.Parse).ToList();
        var categories = categoryIds.Count == 0
            ? []
            : await categoryRepository.GetByIdsAsync(categoryIds, cancellationToken);

        var namesById = categories.ToDictionary(c => c.Id.Value, c => c.Name);

        var categoryNames = categoryIds.Select(id =>
            namesById.GetValueOrDefault(
                id, string.Empty)).ToList();

        return new PublishInstruction(
            nameof(ProductSyncedEvent),
            new ProductSyncedEvent
            {
                ProductId = image.Id,
                Name = image.Name,
                Description = image.Description,
                ImageUrl = image.ImageUrl,
                Stock = image.Stock,
                CategoryIds = image.CategoryIds,
                CategoryNames = categoryNames
            });
    }
}
