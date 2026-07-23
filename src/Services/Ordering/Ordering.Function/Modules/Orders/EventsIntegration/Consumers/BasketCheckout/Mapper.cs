namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.BasketCheckout;

public static class BasketCheckoutMapper
{
    public static CreateOrderCommand ToCreateOrderCommand(BasketCheckoutEvent message)
    {
        var addressDto = new AddressDto(
            message.ShippingAddress.FirstName,
            message.ShippingAddress.LastName,
            message.ShippingAddress.EmailAddress,
            message.ShippingAddress.AddressLine,
            message.ShippingAddress.Country,
            message.ShippingAddress.State,
            message.ShippingAddress.ZipCode);

        var paymentDto = new PaymentDto(
            (PaymentMethod)message.Payment.PaymentMethod,
            message.Payment.Installments);

        return new CreateOrderCommand(
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            // OwnerId is now a prefixed technical id (USER#<sub>); the email is the human-readable name.
            OrderName: message.ShippingAddress.EmailAddress,
            ShippingAddress: addressDto,
            Payment: paymentDto,
            OrderItems: [.. message.Items.Select(item =>
                new CreateOrderItemDto(item.ProductId, item.ProductName, item.ImageId, item.Quantity, item.Price))]);
    }
}
