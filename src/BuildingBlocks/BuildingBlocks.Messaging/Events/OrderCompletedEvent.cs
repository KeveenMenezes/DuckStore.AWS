namespace BuildingBlocks.Messaging.Events;

// Published by Ordering when an order transitions to Completed (payment authorized). Review
// consumes it to make the customer eligible to review each purchased product (ADR-0049 §3).
public record OrderCompletedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }

    // The Cognito sub: checkout is Cognito-only, so the order's CustomerId is the user id.
    public Guid CustomerId { get; init; }

    // Distinct — an order line repeated for the same product yields one id.
    public List<Guid> ProductIds { get; init; } = [];
}
