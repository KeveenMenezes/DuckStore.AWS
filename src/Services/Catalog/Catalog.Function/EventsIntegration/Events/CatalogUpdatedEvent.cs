using BuildingBlocks.Messaging.Events;

namespace Catalog.Function.EventsIntegration.Events;

public record CatalogUpdatedEvent : IntegrationEvent
{
    public string ChangeType { get; init; } = string.Empty;
}
