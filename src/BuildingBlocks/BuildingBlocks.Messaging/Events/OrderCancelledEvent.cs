namespace BuildingBlocks.Messaging.Events;

// Published by Ordering when an order transitions to Cancelled, from Pending (payment declined) or
// from Completed (cancelled after payment). Challenges consumes it to give back the points the
// checkout reserved or used — Released or Refunded (ADR-0048 §5).
public record OrderCancelledEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }

    // The Cognito sub: checkout is Cognito-only, so the order's CustomerId is the user id.
    public Guid CustomerId { get; init; }
}
