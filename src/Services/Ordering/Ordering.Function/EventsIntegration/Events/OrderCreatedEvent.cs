namespace Ordering.Function.EventsIntegration.Events;

public record OrderCreatedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public string OrderName { get; init; } = string.Empty;
    public AddressDto ShippingAddress { get; init; } = default!;
    public PaymentDto Payment { get; init; } = default!;
    public OrderStatus Status { get; init; }
    public List<OrderItemDto> OrderItems { get; init; } = [];
}
