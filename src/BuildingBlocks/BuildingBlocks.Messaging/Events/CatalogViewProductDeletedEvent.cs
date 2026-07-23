namespace BuildingBlocks.Messaging.Events;

// Published by CatalogView's own CDC stream publisher when a catalogview-products item is removed
// (REMOVE). Sibling to CatalogViewProductSyncedEvent — see that event's comment for why CatalogView
// needs its own outbound events instead of the SPA revalidator subscribing to upstream events
// directly.
public record CatalogViewProductDeletedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
}
