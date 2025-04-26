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
        var billingAddress = OrderDataTests.CreateBillingAddressWithVersion();
        var payment = OrderDataTests.CreatePaymentWithVersion();

        // Act
        var order = Order.Create(
            orderId,
            customerId,
            orderName,
            shippingAddress,
            billingAddress,
            payment);

        // Assert
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(orderName, order.OrderName);
        Assert.Equal(shippingAddress, order.ShippingAddress);
        Assert.Equal(billingAddress, order.BillingAddress);
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
        var billingAddress = OrderDataTests.CreateBillingAddressWithVersion();
        var payment = OrderDataTests.CreatePaymentWithVersion();

        var order = Order.Create(
            orderId,
            customerId,
            orderName,
            shippingAddress,
            billingAddress,
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
    public void Remove_ShouldRemoveOrderItem()
    {
        // Arrange
        var orderId = OrderId.Of(Guid.NewGuid());
        var customerId = CustomerId.Of(Guid.NewGuid());
        var orderName = OrderName.Of("Test Order");
        var shippingAddress = OrderDataTests.CreateShippingAddressWithVersion();
        var billingAddress = OrderDataTests.CreateBillingAddressWithVersion();
        var payment = OrderDataTests.CreatePaymentWithVersion();

        var order = Order.Create(
            orderId,
            customerId,
            orderName,
            shippingAddress,
            billingAddress,
            payment);

        var productId = ProductId.Of(Guid.NewGuid());
        var quantity = 2;
        var price = 50m;

        order.Add(productId, quantity, price);

        // Act
        order.Remove(productId);

        // Assert
        Assert.Empty(order.OrderItems);
    }

    [Fact]
    public void Update_ShouldUpdateOrderDetails()
    {
        // Arrange
        var orderId = OrderId.Of(Guid.NewGuid());
        var customerId = CustomerId.Of(Guid.NewGuid());
        var orderName = OrderName.Of("Test Order");
        var shippingAddress = OrderDataTests.CreateShippingAddressWithVersion();
        var billingAddress = OrderDataTests.CreateBillingAddressWithVersion();
        var payment = OrderDataTests.CreatePaymentWithVersion();

        var order = Order.Create(
            orderId,
            customerId,
            orderName,
            shippingAddress,
            billingAddress,
            payment);

        var newOrderName = OrderName.Of("Updated Order");
        var newShippingAddress = OrderDataTests.CreateShippingAddressWithVersion("new");
        var newBillingAddress = OrderDataTests.CreateBillingAddressWithVersion("new");
        var newPayment = OrderDataTests.CreatePaymentWithVersion("new");
        var newStatus = OrderStatus.Completed;

        // Act
        order.Update(
            newOrderName,
            newShippingAddress,
            newBillingAddress,
            newPayment,
            newStatus);

        // Assert
        Assert.Equal(newOrderName, order.OrderName);
        Assert.Equal(newShippingAddress, order.ShippingAddress);
        Assert.Equal(newBillingAddress, order.BillingAddress);
        Assert.Equal(newPayment, order.Payment);
        Assert.Equal(newStatus, order.Status);
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
            OrderDataTests.CreateBillingAddressWithVersion(),
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
            OrderDataTests.CreateBillingAddressWithVersion(),
            OrderDataTests.CreatePaymentWithVersion());

        var productId = ProductId.Of(Guid.NewGuid());
        var quantity = 1;
        var price = -10m;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.Add(productId, quantity, price));
    }

    [Fact]
    public void Remove_ShouldThrowException_WhenProductIdDoesNotExist()
    {
        // Arrange
        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of("Test Order"),
            OrderDataTests.CreateShippingAddressWithVersion(),
            OrderDataTests.CreateBillingAddressWithVersion(),
            OrderDataTests.CreatePaymentWithVersion());

        var nonExistentProductId = ProductId.Of(Guid.NewGuid());

        // Act & Assert
        var exception = Record.Exception(() =>
            order.Remove(nonExistentProductId));

        Assert.NotNull(exception);
    }
}
