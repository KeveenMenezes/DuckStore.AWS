namespace BuildingBlocks.Messaging.Events;

public record ReviewCreatedEvent : IntegrationEvent
{
    public string ReviewId { get; set; }
    public Guid ProductId { get; set; }

    // The reviewer's Cognito sub, so consumers (review points) can credit the author (ADR-0049 §5).
    public string UserId { get; set; }
    public int Rating { get; set; }
}
