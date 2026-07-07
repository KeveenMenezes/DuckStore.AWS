namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.OrderCreated;

public static class OrderCreatedMapper
{
    public static CreatePaymentCommand ToCreatePaymentCommand(OrderCreatedEvent message) =>
        new(
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            // OrderCreatedEvent carries no single Amount/Total field — compute it from its items.
            Amount: message.Items.Sum(item => item.Price * item.Quantity),
            CardNumber: message.CardNumber,
            Expiration: message.Expiration,
            Cvv: message.Cvv,
            PaymentMethod: (PaymentMethod)message.PaymentMethod);
}
