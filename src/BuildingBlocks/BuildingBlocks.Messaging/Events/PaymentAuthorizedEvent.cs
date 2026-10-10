namespace BuildingBlocks.Messaging.Events;

public record PaymentAuthorizedEvent : IntegrationEvent
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
    public string AuthorizationCode { get; init; } = default!;

    // The Cognito sub (checkout is Cognito-only), so consumers can rebuild "USER#<sub>": Challenges'
    // settlement keys the REDEMPTION#<orderId> ledger row off it (ADR-0048 §5).
    public Guid CustomerId { get; init; }
}
