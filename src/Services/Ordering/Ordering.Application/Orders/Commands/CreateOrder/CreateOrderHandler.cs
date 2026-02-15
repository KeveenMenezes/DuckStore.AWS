namespace Ordering.Application.Orders.Commands.CreateOrder;

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

    private static Order CreateNewOrder(CreateOrderCommand orderDto)
    {
        var shippingAddress = Address.Of(
            orderDto.ShippingAddress.FirstName,
            orderDto.ShippingAddress.LastName,
            orderDto.ShippingAddress.EmailAddress,
            orderDto.ShippingAddress.AddressLine,
            orderDto.ShippingAddress.Country,
            orderDto.ShippingAddress.State,
            orderDto.ShippingAddress.ZipCode);

        var newOrder = Order.Create(
            id: OrderId.Of(Guid.NewGuid()),
            customerId: CustomerId.Of(orderDto.CustomerId),
            orderName: OrderName.Of(orderDto.OrderName),
            shippingAddress: shippingAddress,
            payment: Payment.Of(
                orderDto.Payment.CardName,
                orderDto.Payment.CardNumber,
                orderDto.Payment.Expiration,
                orderDto.Payment.Cvv,
                orderDto.Payment.PaymentMethod));

        foreach (var orderItemDto in orderDto.OrderItems)
        {
            newOrder.Add(ProductId.Of(orderItemDto.ProductId), orderItemDto.Quantity, orderItemDto.Price);
        }
        return newOrder;
    }
}
