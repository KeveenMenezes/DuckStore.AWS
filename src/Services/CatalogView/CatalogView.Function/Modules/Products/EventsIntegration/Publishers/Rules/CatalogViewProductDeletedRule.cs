namespace CatalogView.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Publishes CatalogViewProductDeletedEvent whenever a catalogview-products item is removed — one
// rule per occurrence (ADR-0031), sibling to CatalogViewProductSyncedRule.
public sealed class CatalogViewProductDeletedRule : IStreamRule<CatalogViewProductStreamImage>
{
    public bool Match(StreamContext<CatalogViewProductStreamImage> context) =>
        context.EventName == "REMOVE" && !string.IsNullOrEmpty(context.Old?.Id);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogViewProductStreamImage> context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PublishInstruction(
            nameof(CatalogViewProductDeletedEvent),
            new CatalogViewProductDeletedEvent { ProductId = context.Old!.Id }));
}
