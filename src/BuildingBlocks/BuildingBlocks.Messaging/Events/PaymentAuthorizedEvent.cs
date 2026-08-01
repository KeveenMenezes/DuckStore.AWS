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

    // Carried alongside DiscountId so the burn consumer can rebuild "USER#<sub>" — customer-discounts
    // is keyed by (OwnerId, DiscountId), and the Cognito sub this equals is guaranteed correct since
    // checkout (and therefore this CustomerId) is Cognito-only. Unused when DiscountId is null.
    public Guid CustomerId { get; init; }
}
