namespace BuildingBlocks.Messaging.Events;

// Carries the full product payload (hydrated from the DynamoDB Streams image) so CatalogView can
// index a complete search document without an out-of-band call back into Catalog. Sibling to
// CatalogUpdatedEvent (which only carries ChangeType+ProductId for ISR tag invalidation) — see
// ADR-0027. Rating fields are deliberately absent: CatalogView owns them exclusively. Price is
// also absent: Pricing owns it (ADR-0026) and syncs it separately via PriceChangedEvent.
public record CatalogProductSyncEvent : IntegrationEvent
{
    public string ChangeType { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public int Stock { get; init; }
    public List<string> CategoryIds { get; init; } = [];
}
