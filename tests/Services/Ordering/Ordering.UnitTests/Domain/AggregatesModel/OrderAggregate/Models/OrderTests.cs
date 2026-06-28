namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.Models;

public class OrderTests
{
    [Fact]
    public void Create_ShouldInitializeOrderWithValidData()
    {
        // Arrange
        var orderId = OrderId.Of(Guid.NewGuid());
        var customerId = CustomerId.Of(Guid.NewGuid());
        var orderName = OrderName.Of("Test Order");
        var shippingAddress = OrderDataTests.CreateShippingAddressWithVersion();
        var payment = OrderDataTests.CreatePaymentWithVersion();

        // Act
        var order = Order.Create(
            orderId,
            customerId,
            orderName,
            shippingAddress,
            payment);

        // Assert
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(orderName, order.OrderName);
        Assert.Equal(shippingAddress, order.ShippingAddress);
        Assert.Equal(payment, order.Payment);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(order.OrderItems);
    }

    [Fact]
    public void Add_ShouldAddOrderItem()
    {
        // Arrange
        var orderId = OrderId.Of(Guid.NewGuid());
        var customerId = CustomerId.Of(Guid.NewGuid());
        var orderName = OrderName.Of("Test Order");
        var shippingAddress = OrderDataTests.CreateShippingAddressWithVersion();
        var payment = OrderDataTests.CreatePaymentWithVersion();

        var order = Order.Create(
            orderId,
            customerId,
            orderName,
            shippingAddress,
            payment);

        var productId = ProductId.Of(Guid.NewGuid());
        var quantity = 2;
        var price = 50m;

        // Act
        order.Add(productId, quantity, price);

        // Assert
        Assert.Single(order.OrderItems);

        var orderItem = order.OrderItems[0];
        Assert.Equal(productId, orderItem?.ProductId);
        Assert.Equal(quantity, orderItem?.Quantity);
        Assert.Equal(price, orderItem?.Price);
    }

    [Fact]
    public void Add_ShouldThrowException_WhenQuantityIsZero()
    {
        // Arrange
        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of("Test Order"),
            OrderDataTests.CreateShippingAddressWithVersion(),
            OrderDataTests.CreatePaymentWithVersion());

        var productId = ProductId.Of(Guid.NewGuid());
        var quantity = 0;
        var price = 50m;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.Add(productId, quantity, price));
    }

    [Fact]
    public void Add_ShouldThrowException_WhenPriceIsNegative()
    {
        // Arrange
        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of("Test Order"),
            OrderDataTests.CreateShippingAddressWithVersion(),
            OrderDataTests.CreatePaymentWithVersion());

        var productId = ProductId.Of(Guid.NewGuid());
        var quantity = 1;
        var price = -10m;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.Add(productId, quantity, price));
    }
}
