namespace Payment.UnitTests.Application.EventHandlers.Integration;

public class OrderCreatedMapperTests
{
    [Fact]
    public void ToCreatePaymentCommand_ShouldMapFields_WhenEventIsValid()
    {
        var orderCreatedEvent = OrderCreatedEventDataTests.CreateValidOrderCreatedEvent();

        var command = OrderCreatedMapper.ToCreatePaymentCommand(orderCreatedEvent);

        Assert.Equal(orderCreatedEvent.OrderId, command.OrderId);
        Assert.Equal(orderCreatedEvent.CustomerId, command.CustomerId);
        Assert.Equal(orderCreatedEvent.CardNumber, command.CardNumber);
        Assert.Equal(1400m, command.Amount); // 2*500 + 1*400, from the seeded OrderCreatedItems
    }
}
