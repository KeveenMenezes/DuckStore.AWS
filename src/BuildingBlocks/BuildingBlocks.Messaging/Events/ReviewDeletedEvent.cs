namespace BuildingBlocks.Messaging.Events;

// Published when a Published review is withdrawn (Published -> Deleted). CatalogView subtracts it
// from the rating aggregate exactly once; the later TTL REMOVE of the row publishes nothing
// (ADR-0049 §5).
public record ReviewDeletedEvent : IntegrationEvent
{
    public string ReviewId { get; set; }
    public Guid ProductId { get; set; }

    // The rating being withdrawn, taken from the old image.
    public int Rating { get; set; }
}
