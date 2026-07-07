namespace PaymentGateway.UnitTests.Modules.Gateway.EventsIntegration.Consumers;

public class PaymentRequestedConsumerTests
{
    private static EventBridgeEvent<PaymentRequestedEvent> Event(string cardNumber, decimal amount)
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        return new EventBridgeEvent<PaymentRequestedEvent>
        {
            Id = Guid.NewGuid().ToString(),
            Detail = new PaymentRequestedEvent
            {
                PaymentId = paymentId,
                OrderId = orderId,
                CustomerId = Guid.NewGuid(),
                Amount = amount,
                CardNumber = cardNumber,
                Expiration = "12/28",
                Cvv = "123",
                PaymentMethod = 2
            }
        };
    }

    [Fact]
    public async Task PaymentRequestedConsumer_PublishesAuthorized_WhenGatewayAuthorizes()
    {
        var publisher = new Mock<IEventPublisher>();
        var functions = new PaymentGateway.Function.Functions();
        var evt = Event("4111111111111111", 100m);

        await functions.PaymentRequestedConsumer(evt, publisher.Object);

        publisher.Verify(p => p.PublishAsync(
            It.Is<PaymentAuthorizedEvent>(e => e.PaymentId == evt.Detail.PaymentId && e.OrderId == evt.Detail.OrderId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PaymentRequestedConsumer_PublishesDeclined_WhenGatewayDeclines()
    {
        var publisher = new Mock<IEventPublisher>();
        var functions = new PaymentGateway.Function.Functions();
        var evt = Event("4111111111110000", 100m);

        await functions.PaymentRequestedConsumer(evt, publisher.Object);

        publisher.Verify(p => p.PublishAsync(
            It.Is<PaymentDeclinedEvent>(e => e.PaymentId == evt.Detail.PaymentId && e.OrderId == evt.Detail.OrderId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
