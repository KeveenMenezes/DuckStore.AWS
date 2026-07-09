namespace BuildingBlocks.Messaging.Events;

public record CatalogCategorySyncEvent : IntegrationEvent
{
    public string CategoryId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
