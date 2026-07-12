namespace BuildingBlocks.Messaging.Events;

// Lets consumers invalidate a per-product cache tag (e.g. products:{id}) instead of only
// the blanket "products" tag.
public record ProductUpdatedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
}
