namespace CatalogView.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Publishes CatalogViewProductSyncedEvent whenever a catalogview-products item is created or
// updated — one rule per occurrence (ADR-0031), sibling to CatalogViewProductDeletedRule.
public sealed class CatalogViewProductSyncedRule : IStreamRule<CatalogViewProductStreamImage>
{
    public bool Match(StreamContext<CatalogViewProductStreamImage> context) =>
        (context.EventName == "INSERT" || context.EventName == "MODIFY")
        && !string.IsNullOrEmpty(context.New?.Id);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogViewProductStreamImage> context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PublishInstruction(
            nameof(CatalogViewProductSyncedEvent),
            new CatalogViewProductSyncedEvent { ProductId = context.New!.Id }));
}
