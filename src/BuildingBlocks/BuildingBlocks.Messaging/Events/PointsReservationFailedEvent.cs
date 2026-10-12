namespace BuildingBlocks.Messaging.Events;

// Published by Challenges (points-transactions stream) when a checkout asked for more points than
// the balance held: nothing was debited and the REDEMPTION#<orderId> row is Failed (ADR-0048 §5).
// Payment consumes it to decline the payment with InsufficientPoints. Points is what was asked
// for; a quantity only, never money (ADR-0046 §1).
public record PointsReservationFailedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string OwnerId { get; init; } = default!;
    public int Points { get; init; }
}
