namespace Payment.UnitTests.Application.EventHandlers.Integration;

public class BasketCheckoutMapperTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void ToPromotePaymentCommand_ShouldCarryTheOrderAndThePoints(int pointsToUse)
    {
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent() with
        {
            PointsToUse = pointsToUse
        };

        var command = BasketCheckoutMapper.ToPromotePaymentCommand(basketCheckoutEvent);

        Assert.Equal(basketCheckoutEvent.OrderId, command.OrderId);
        Assert.Equal(pointsToUse, command.PointsToUse);
    }

    [Fact]
    public void ToPromotePaymentCommand_ShouldThrow_WhenTheEventHasNoOrderId()
    {
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent() with
        {
            OrderId = Guid.Empty
        };

        Assert.Throws<ArgumentException>(() => BasketCheckoutMapper.ToPromotePaymentCommand(basketCheckoutEvent));
    }
}
