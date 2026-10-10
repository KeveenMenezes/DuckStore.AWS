namespace BuildingBlocks.Messaging.Events;

public record PaymentAuthorizedEvent : IntegrationEvent
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
    public string AuthorizationCode { get; init; } = default!;

    // The customer discount used at checkout, if any (ADR-0046 §6) — pricing-payment-authorized-consumer
    // burns it (Issued -> Consumed) here, at authorization, not at checkout: a declined payment must
    // leave the discount usable. Null when no coupon was applied.
    public string? DiscountId { get; init; }

    // The Cognito sub (checkout is Cognito-only), so consumers can rebuild "USER#<sub>": the coupon
    // burn keys customer-discounts by (OwnerId, DiscountId), and Challenges' settlement keys the
    // REDEMPTION#<orderId> ledger row off it (ADR-0048 §5). Must stay when DiscountId is removed.
    public Guid CustomerId { get; init; }
}
