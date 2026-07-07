namespace BuildingBlocks.Messaging.Events;

public record ReviewUpdatedEvent : IntegrationEvent
{
    public string ReviewId { get; set; }
    public Guid ProductId { get; set; }
    public int OldRating { get; set; }
    public int NewRating { get; set; }
}
