namespace Payment.UnitTests.Application.EventHandlers.Integration;

public class BasketCheckoutMapperTests
{
    [Fact]
    public void ToCreatePaymentCommand_ShouldMapFields_WhenEventIsValid()
    {
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent();

        var command = BasketCheckoutMapper.ToCreatePaymentCommand(basketCheckoutEvent);

        Assert.Equal(basketCheckoutEvent.OrderId, command.OrderId);
        Assert.Equal(basketCheckoutEvent.CustomerId, command.CustomerId);
        Assert.Equal(basketCheckoutEvent.CardNumber, command.CardNumber);
        Assert.Equal(basketCheckoutEvent.TotalPrice, command.Amount);
    }
}
