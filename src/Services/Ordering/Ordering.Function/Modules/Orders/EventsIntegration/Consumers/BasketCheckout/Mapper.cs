namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.BasketCheckout;

public static class BasketCheckoutMapper
{
    public static CreateOrderCommand ToCreateOrderCommand(BasketCheckoutEvent message)
    {
        var addressDto = new AddressDto(
            message.FirstName,
            message.LastName,
            message.EmailAddress,
            message.AddressLine,
            message.Country,
            message.State,
            message.ZipCode);

        var paymentDto = new PaymentDto(
            message.CardName,
            message.CardNumber,
            message.Expiration,
            message.Cvv,
            (PaymentMethod)message.PaymentMethod,
            message.Installments);

        return new CreateOrderCommand(
            CustomerId: message.CustomerId,
            // OwnerId is now a prefixed technical id (USER#<sub>); the email is the human-readable name.
            OrderName: message.EmailAddress,
            ShippingAddress: addressDto,
            Payment: paymentDto,
            OrderItems:
            // TODO: incluir dados da OrderItem
            [
                new CreateOrderItemDto(
                    new Guid("5334c996-8457-4cf0-815c-ed2b77c4ff61"),
                    2,
                    500),

                new CreateOrderItemDto(
                    new Guid("c67d6323-e8b1-4bdf-9a75-b0d0d2e7e914"),
                    1,
                    400)
            ]);
    }
}
