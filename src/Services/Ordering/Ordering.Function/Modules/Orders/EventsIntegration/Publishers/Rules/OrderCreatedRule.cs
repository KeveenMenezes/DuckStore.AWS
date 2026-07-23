namespace Ordering.Function.Modules.Orders.EventsIntegration.Publishers.Rules;

// Publishes OrderCreatedEvent when a new order row lands in the table. The aggregate is rehydrated
// from the repository (a domain abstraction — not AWS infrastructure); the rule never references
// EventBridge and only returns a generic PublishInstruction.
public sealed class OrderCreatedRule(IOrderRepository orders) : IStreamRule<OrderStreamImage>
{
    public bool Match(StreamContext<OrderStreamImage> context) =>
        context.EventName == "INSERT" && context.New?.Type == "Order";

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<OrderStreamImage> context, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetByIdAsync(context.New!.Id, cancellationToken);

        return new PublishInstruction(nameof(OrderCreatedEvent), ToOrderCreatedEvent(order!));
    }

    private static OrderCreatedEvent ToOrderCreatedEvent(Order order) => new()
    {
        OrderId = order.Id.Value,
        CustomerId = order.CustomerId.Value,
        OrderName = order.OrderName.Value,
        Status = (int)order.Status,

        FirstName = order.ShippingAddress.FirstName,
        LastName = order.ShippingAddress.LastName,
        EmailAddress = order.ShippingAddress.EmailAddress,
        AddressLine = order.ShippingAddress.AddressLine,
        Country = order.ShippingAddress.Country,
        State = order.ShippingAddress.State,
        ZipCode = order.ShippingAddress.ZipCode,

        PaymentMethod = (int)order.Payment.PaymentMethod,

        Items = [.. order.OrderItems.Select(oi =>
            new OrderCreatedItem(oi.ProductId.Value, oi.Quantity, oi.Price))]
    };
}
