namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.BasketCheckout;

public class CreateOrderHandler(
    IOrderRepository orderRepository)
    : ICommandHandler<CreateOrderCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = CreateNewOrder(command);

        await orderRepository.AddAsync(order, cancellationToken);

        return new CreateOrderResult(order.Id.Value);
    }

    internal static Order CreateNewOrder(CreateOrderCommand orderDto)
    {
        var shippingAddress = Address.Of(
            orderDto.ShippingAddress.FirstName,
            orderDto.ShippingAddress.LastName,
            orderDto.ShippingAddress.EmailAddress,
            orderDto.ShippingAddress.AddressLine,
            orderDto.ShippingAddress.Country,
            orderDto.ShippingAddress.State,
            orderDto.ShippingAddress.ZipCode);

        return Order.CreateFromCheckout(
            orderId: OrderId.Of(orderDto.OrderId),
            customerId: CustomerId.Of(orderDto.CustomerId),
            orderName: OrderName.Of(orderDto.OrderName),
            shippingAddress: shippingAddress,
            payment: Payment.Of(orderDto.Payment.PaymentMethod, orderDto.Payment.Installments),
            items: orderDto.OrderItems.Select(item =>
                (ProductId.Of(item.ProductId), item.ProductName, item.ImageId, item.Quantity, item.Price)));
    }
}
