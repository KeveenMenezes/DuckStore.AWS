namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class OrderIdTests
{
    [Fact]
    public void Of_ShouldCreateOrderIdWithValidGuid()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var orderId = OrderId.Of(validGuid);

        // Assert
        Assert.NotNull(orderId);
        Assert.Equal(validGuid, orderId.Value);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenGuidIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act & Assert
        Assert.Throws<OrderIdBadRequestException>(() => OrderId.Of(emptyGuid));
    }
}
