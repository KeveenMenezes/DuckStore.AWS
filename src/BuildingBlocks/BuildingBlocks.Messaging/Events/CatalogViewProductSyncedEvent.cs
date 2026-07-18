namespace BuildingBlocks.Messaging.Events;

// Published by CatalogView's own CDC stream publisher whenever a catalogview-products item is
// created or updated (INSERT/MODIFY), regardless of which upstream bounded context caused the
// write (Catalog, Pricing, or Review — see the six consumers under
// CatalogView.Function/Modules/Products/EventsIntegration/Consumers). Consumed by the SPA's
// `revalidator` Lambda so cache invalidation only fires after CatalogView's own write has
// committed, instead of racing it via a direct subscription to the upstream events.
public record CatalogViewProductSyncedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
}
