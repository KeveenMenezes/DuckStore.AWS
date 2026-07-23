namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public static class BasketCheckoutMapper
{
    public static CreatePaymentCommand ToCreatePaymentCommand(BasketCheckoutEvent message) =>
        new(
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            Amount: message.TotalPrice,
            CardNumber: message.CardNumber,
            Expiration: message.Expiration,
            Cvv: message.Cvv,
            PaymentMethod: (PaymentMethod)message.PaymentMethod);
}
