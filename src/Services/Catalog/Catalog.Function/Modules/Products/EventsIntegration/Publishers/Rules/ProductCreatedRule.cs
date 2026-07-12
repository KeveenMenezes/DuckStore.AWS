namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Fires once per product INSERT and produces a thin ProductCreatedEvent (id only) for
// consumers that only need to know a product now exists — e.g. the SPA's ISR revalidator
// (ADR-0031: named after the domain occurrence, no ChangeType discriminator).
public sealed class ProductCreatedRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        context.EventName == "INSERT" && !string.IsNullOrEmpty(context.New?.Id);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogStreamImage> context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PublishInstruction(
            nameof(ProductCreatedEvent),
            new ProductCreatedEvent { ProductId = context.New!.Id }));
}
