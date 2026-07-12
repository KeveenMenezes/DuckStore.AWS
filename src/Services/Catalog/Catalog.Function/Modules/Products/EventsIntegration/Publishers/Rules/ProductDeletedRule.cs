namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Fires once per product REMOVE and produces a thin ProductDeletedEvent (id only), shared by
// every consumer that only needs to know a product was deleted — Pricing (row cleanup) and
// CatalogView (search-index delete) each subscribe to this same event (ADR-0031: named after the
// domain occurrence, no ChangeType discriminator). REMOVE has no NewImage, so the id comes from
// Old.
public sealed class ProductDeletedRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        context.EventName == "REMOVE" && !string.IsNullOrEmpty(context.Old?.Id);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogStreamImage> context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PublishInstruction(
            nameof(ProductDeletedEvent),
            new ProductDeletedEvent { ProductId = context.Old!.Id }));
}
