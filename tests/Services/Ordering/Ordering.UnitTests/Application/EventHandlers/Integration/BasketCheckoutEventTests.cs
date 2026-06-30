using Ordering.Function.EventsIntegration.Consumer.BasketCheckout;

namespace Ordering.UnitTests.Application.EventHandlers.Integration;

public class BasketCheckoutMapperTests
{
    [Fact]
    public void ToCreateOrderCommand_ShouldMapFields_WhenEventIsValid()
    {
        // Arrange
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent();

        // Act
        var command = BasketCheckoutMapper.ToCreateOrderCommand(basketCheckoutEvent);

        // Assert
        Assert.Equal(basketCheckoutEvent.CustomerId, command.CustomerId);
        Assert.Equal(basketCheckoutEvent.UserName, command.OrderName);
        Assert.Equal(basketCheckoutEvent.CardNumber, command.Payment.CardNumber);
        Assert.NotEmpty(command.OrderItems);
    }
}
