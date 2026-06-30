using BuildingBlocks.Messaging.Events;

namespace Catalog.Function.Modules.Products.EventsIntegration.Events;

public record CatalogUpdatedEvent : IntegrationEvent
{
    public string ChangeType { get; init; } = string.Empty;
}
