using BuildingBlocks.Messaging.Streams;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers.Rules;

namespace Ordering.UnitTests.Application.EventHandlers.Publishers;

public class OrderCompletedRuleTests
{
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly OrderCompletedRule _rule;

    public OrderCompletedRuleTests()
    {
        var autoMocker = new AutoMocker();
        _orderRepository = autoMocker.GetMock<IOrderRepository>();
        _rule = autoMocker.CreateInstance<OrderCompletedRule>();
    }

    private static StreamContext<OrderStreamImage> Context(
        string eventName, string? oldStatus, string? newStatus, Guid? id = null, string type = "Order")
    {
        var orderId = id ?? Guid.NewGuid();
        return new(
            eventName,
            oldStatus is null ? null : new OrderStreamImage(orderId, type, oldStatus),
            newStatus is null ? null : new OrderStreamImage(orderId, type, newStatus));
    }

    [Fact]
    public void Match_ReturnsTrue_ForPendingToCompleted() =>
        Assert.True(_rule.Match(Context("MODIFY", "Pending", "Completed")));

    [Fact]
    public void Match_ReturnsFalse_ForCompletedToCompleted() =>
        Assert.False(_rule.Match(Context("MODIFY", "Completed", "Completed")));

    [Fact]
    public void Match_ReturnsFalse_ForPendingToCancelled() =>
        Assert.False(_rule.Match(Context("MODIFY", "Pending", "Cancelled")));

    [Fact]
    public void Match_ReturnsFalse_ForInsertOfCompletedOrder() =>
        Assert.False(_rule.Match(Context("INSERT", null, "Completed")));

    [Fact]
    public void Match_ReturnsFalse_ForRemove() =>
        Assert.False(_rule.Match(Context("REMOVE", "Completed", null)));

    [Fact]
    public void Match_ReturnsFalse_WhenImageIsNotAnOrder() =>
        Assert.False(_rule.Match(Context("MODIFY", "Pending", "Completed", type: "Coupon")));

    [Fact]
    public async Task BuildAsync_ReturnsOrderCompletedInstruction_WithDistinctProductIds()
    {
        var duplicatedProduct = ProductId.Of(Guid.NewGuid());
        var otherProduct = ProductId.Of(Guid.NewGuid());
        var order = Order.CreateFromCheckout(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of("ORD_1"),
            OrderDataTests.CreateShippingAddressWithVersion("1"),
            OrderDataTests.CreatePaymentWithVersion("1"),
            [
                (duplicatedProduct, "Rubber Duck Classic", null, 1, 10m),
                (otherProduct, "Rubber Duck Pirate", null, 2, 20m),
                (duplicatedProduct, "Rubber Duck Classic", null, 3, 10m)
            ]);
        order.ApplyPaymentResult(authorized: true);

        _orderRepository
            .Setup(repo => repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var instruction = await _rule.BuildAsync(Context("MODIFY", "Pending", "Completed", order.Id.Value));

        Assert.Equal(nameof(OrderCompletedEvent), instruction.DetailType);
        var payload = Assert.IsType<OrderCompletedEvent>(instruction.Payload);
        Assert.Equal(order.Id.Value, payload.OrderId);
        Assert.Equal(order.CustomerId.Value, payload.CustomerId);
        Assert.Equal([duplicatedProduct.Value, otherProduct.Value], payload.ProductIds);
    }
}
