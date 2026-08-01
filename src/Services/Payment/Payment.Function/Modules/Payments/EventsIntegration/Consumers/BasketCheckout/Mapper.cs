namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public static class BasketCheckoutMapper
{
    public static CreatePaymentCommand ToCreatePaymentCommand(BasketCheckoutEvent message) =>
        new(
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            Amount: message.TotalPrice,
            CardNumber: message.Payment.CardNumber,
            Expiration: message.Payment.Expiration,
            Cvv: message.Payment.Cvv,
            PaymentMethod: (PaymentMethod)message.Payment.PaymentMethod,
            DiscountId: message.DiscountId);
}
