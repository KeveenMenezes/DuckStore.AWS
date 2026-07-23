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
        Assert.Equal(basketCheckoutEvent.OrderId, command.OrderId);
        Assert.Equal(basketCheckoutEvent.CustomerId, command.CustomerId);
        Assert.Equal(basketCheckoutEvent.EmailAddress, command.OrderName);
        Assert.Equal((PaymentMethod)basketCheckoutEvent.PaymentMethod, command.Payment.PaymentMethod);
        Assert.NotEmpty(command.OrderItems);
    }
}
