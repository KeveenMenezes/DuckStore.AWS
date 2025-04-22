namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.Models;

public class OrderItemTests
{
    [Fact]
    public void Constructor_ShouldInitializeOrderItemWithValidData()
    {
        // Arrange
        var orderId = OrderId.Of(Guid.NewGuid());
        var productId = ProductId.Of(Guid.NewGuid());
        var quantity = 2;
        var price = 50m;

        // Act
        var orderItem = new OrderItem(orderId, productId, quantity, price);

        // Assert
        Assert.NotNull(orderItem);
        Assert.Equal(orderId, orderItem.OrderId);
        Assert.Equal(productId, orderItem.ProductId);
        Assert.Equal(quantity, orderItem.Quantity);
        Assert.Equal(price, orderItem.Price);
    }
}
