using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;
using Ordering.Domain.AggregatesModel.OrderAggregate.Models;
using Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

namespace Ordering.Application.Orders.Commands.UpdateOrder;

public class UpdateOrderHandler(
    IOrderRepository orderRepository)
    : ICommandHandler<UpdateOrderCommand, UpdateOrderResult>
{
    public async Task<UpdateOrderResult> Handle(
        UpdateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(command.Order.Id, cancellationToken) ??
            throw new OrderNotFoundBadRequestException(command.Order.Id);

        UpdateOrderWithNewValues(order, command.Order);

        await orderRepository.UpdateAsync(order, cancellationToken);

        return new UpdateOrderResult(true);
    }

    public static void UpdateOrderWithNewValues(Order order, OrderDto orderDto)
    {
        var updatedShippingAddress = Address.Of(
            orderDto.ShippingAddress.FirstName,
            orderDto.ShippingAddress.LastName,
            orderDto.ShippingAddress.EmailAddress,
            orderDto.ShippingAddress.AddressLine,
            orderDto.ShippingAddress.Country,
            orderDto.ShippingAddress.State,
            orderDto.ShippingAddress.ZipCode);

        var updatedBillingAddress = Address.Of(
            orderDto.BillingAddress.FirstName,
            orderDto.BillingAddress.LastName,
            orderDto.BillingAddress.EmailAddress,
            orderDto.BillingAddress.AddressLine,
            orderDto.BillingAddress.Country,
            orderDto.BillingAddress.State,
            orderDto.BillingAddress.ZipCode);

        var updatedPayment = Payment.Of(
            orderDto.Payment.CardName,
            orderDto.Payment.CardNumber,
            orderDto.Payment.Expiration,
            orderDto.Payment.Cvv,
            orderDto.Payment.PaymentMethod);

        order.Update(
            orderName: OrderName.Of(orderDto.OrderName),
            shippingAddress: updatedShippingAddress,
            billingAddress: updatedBillingAddress,
            payment: updatedPayment,
            status: orderDto.Status);
    }
}
