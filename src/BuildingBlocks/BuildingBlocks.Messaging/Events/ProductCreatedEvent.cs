namespace BuildingBlocks.Messaging.Events;

public record ProductCreatedEvent : IntegrationEvent
{
    public string ProductId { get; init; } = string.Empty;
}
