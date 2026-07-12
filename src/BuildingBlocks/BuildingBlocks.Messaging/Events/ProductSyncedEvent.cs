namespace BuildingBlocks.Messaging.Events;

// Carries the full product payload (hydrated from the DynamoDB Streams image) so CatalogView can
// index a complete search document without an out-of-band call back into Catalog. Fires on
// create/update only (ADR-0031) — deletes are handled separately via the shared
// ProductDeletedEvent, since a delete needs no payload. Rating fields are deliberately absent:
// CatalogView owns them exclusively. Price is also absent: Pricing owns it (ADR-0026) and syncs
// it separately via PriceChangedEvent.
public record ProductSyncedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public int Stock { get; init; }
    public List<string> CategoryIds { get; init; } = [];

    public List<string> CategoryNames { get; init; } = [];
}
