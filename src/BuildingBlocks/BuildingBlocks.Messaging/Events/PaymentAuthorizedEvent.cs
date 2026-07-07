namespace BuildingBlocks.Messaging.Events;

public record PaymentAuthorizedEvent : IntegrationEvent
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
    public string AuthorizationCode { get; init; } = default!;
}
