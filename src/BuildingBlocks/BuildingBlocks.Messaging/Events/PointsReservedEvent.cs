namespace BuildingBlocks.Messaging.Events;

// Published by Challenges (points-transactions stream) when the points a checkout asked for were
// debited and the REDEMPTION#<orderId> row is Reserved (ADR-0048 §5). Payment consumes it to move
// the order's payment from AwaitingPoints to Pending. A point quantity only, never money
// (ADR-0046 §1).
public record PointsReservedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string OwnerId { get; init; } = default!;
    public int Points { get; init; }
}
