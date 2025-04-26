namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class OrderItemIdTests
{
    [Fact]
    public void Of_ShouldCreateOrderItemIdWithValidGuid()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var orderItemId = OrderItemId.Of(validGuid);

        // Assert
        Assert.NotNull(orderItemId);
        Assert.Equal(validGuid, orderItemId.Value);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenGuidIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act & Assert
        Assert.Throws<OrderIdBadRequestException>(() => OrderItemId.Of(emptyGuid));
    }
}
