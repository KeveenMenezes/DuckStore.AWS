namespace Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

// Fires for any INSERT, MODIFY, or REMOVE on the products table and produces a
// CatalogUpdatedEvent. The rule never references EventBridge — it returns a generic
// PublishInstruction that the StreamRuleDispatcher hands to IEventPublisher.
public sealed class CatalogProductChangedRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        !string.IsNullOrEmpty(context.New?.Id ?? context.Old?.Id);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<CatalogStreamImage> context, CancellationToken cancellationToken = default)
    {
        var productId = context.New?.Id ?? context.Old?.Id ?? string.Empty;
        return Task.FromResult(new PublishInstruction(
            nameof(CatalogUpdatedEvent),
            new CatalogUpdatedEvent { ChangeType = context.EventName, ProductId = productId }));
    }
}
