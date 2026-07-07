namespace BuildingBlocks.Messaging.Events;

public record PaymentDeclinedEvent : IntegrationEvent
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
    public string DeclineReason { get; init; } = default!;
}
