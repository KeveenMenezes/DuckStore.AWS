using Ordering.Function.EventsIntegration.Events;

namespace Ordering.Function.Extensions;

public static class OrderExtensions
{
    public static OrderDto ToOrderDto(this Order order)
    {
        return new OrderDto(
            Id: order.Id.Value,
            CustomerId: order.CustomerId.Value,
            OrderName: order.OrderName.Value,
            ShippingAddress: new AddressDto(
                order.ShippingAddress.FirstName,
                order.ShippingAddress.LastName,
                order.ShippingAddress.EmailAddress!,
                order.ShippingAddress.AddressLine,
                order.ShippingAddress.Country,
                order.ShippingAddress.State,
                order.ShippingAddress.ZipCode),
            Payment: new PaymentDto(
                order.Payment.CardName!,
                order.Payment.CardNumber,
                order.Payment.Expiration,
                order.Payment.Cvv,
                order.Payment.PaymentMethod),
            Status: order.Status,
            OrderItems: [.. order.OrderItems
                .Select(oi =>
                    new OrderItemDto(
                        oi.OrderId.Value,
                        oi.ProductId.Value,
                        oi.Quantity, oi.Price))]
        );
    }

    public static OrderCreatedEvent ToOrderCreatedEvent(this Order order) => new()
    {
        OrderId = order.Id.Value,
        CustomerId = order.CustomerId.Value,
        OrderName = order.OrderName.Value,
        ShippingAddress = new AddressDto(
            order.ShippingAddress.FirstName,
            order.ShippingAddress.LastName,
            order.ShippingAddress.EmailAddress!,
            order.ShippingAddress.AddressLine,
            order.ShippingAddress.Country,
            order.ShippingAddress.State,
            order.ShippingAddress.ZipCode),
        Payment = new PaymentDto(
            order.Payment.CardName!,
            order.Payment.CardNumber,
            order.Payment.Expiration,
            order.Payment.Cvv,
            order.Payment.PaymentMethod),
        Status = order.Status,
        OrderItems = [.. order.OrderItems
            .Select(oi =>
                new OrderItemDto(
                    oi.OrderId.Value,
                    oi.ProductId.Value,
                    oi.Quantity, oi.Price))]
    };
}
