namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Fires for any INSERT, MODIFY, or REMOVE on the products table and produces a
// CatalogProductSyncEvent carrying the full product payload, so CatalogView can upsert/delete its
// OpenSearch document without a synchronous call back into Catalog (ADR-0027). Runs alongside
// CatalogProductChangedRule in the same StreamRuleDispatcher<CatalogStreamImage> — one Streams
// record can fan out to more than one integration event.
public sealed class CatalogSearchSyncRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        !string.IsNullOrEmpty(context.New?.Id ?? context.Old?.Id);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogStreamImage> context, CancellationToken cancellationToken = default)
    {
        // REMOVE has no NewImage — fall back to Old so the delete still carries a ProductId.
        var image = context.New ?? context.Old!;

        return Task.FromResult(new PublishInstruction(
            nameof(CatalogProductSyncEvent),
            new CatalogProductSyncEvent
            {
                ChangeType = context.EventName,
                ProductId = image.Id,
                Name = image.Name,
                Description = image.Description,
                ImageUrl = image.ImageUrl,
                Stock = image.Stock,
                CategoryIds = image.CategoryIds
            }));
    }
}
