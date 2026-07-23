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
        Assert.Equal(basketCheckoutEvent.ShippingAddress.EmailAddress, command.OrderName);
        Assert.Equal((PaymentMethod)basketCheckoutEvent.Payment.PaymentMethod, command.Payment.PaymentMethod);
        Assert.Equal(basketCheckoutEvent.Items.Count, command.OrderItems.Count);
        Assert.Equal(basketCheckoutEvent.Items[0].ProductId, command.OrderItems[0].ProductId);
        Assert.Equal(basketCheckoutEvent.Items[0].Quantity, command.OrderItems[0].Quantity);
        Assert.Equal(basketCheckoutEvent.Items[0].Price, command.OrderItems[0].Price);
    }
}
