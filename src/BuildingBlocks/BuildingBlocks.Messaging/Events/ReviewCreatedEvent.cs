namespace BuildingBlocks.Messaging.Events;

public record ReviewCreatedEvent : IntegrationEvent
{
    public Guid ReviewId { get; set; }
    public Guid ProductId { get; set; }
    public int Rating { get; set; }
}
