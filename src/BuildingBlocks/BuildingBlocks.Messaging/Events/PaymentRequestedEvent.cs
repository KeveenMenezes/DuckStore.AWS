namespace BuildingBlocks.Messaging.Events;

public record PaymentRequestedEvent : IntegrationEvent
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal Amount { get; init; }

    public string CardNumber { get; init; } = default!;
    public string Expiration { get; init; } = default!;
    public string Cvv { get; init; } = default!;
    public int PaymentMethod { get; init; }

    // Carried through from BasketCheckoutEvent so PaymentGateway can pass it on to
    // PaymentAuthorizedEvent (ADR-0046 §6) — PaymentGateway never reads it itself.
    public string? DiscountId { get; init; }
}
