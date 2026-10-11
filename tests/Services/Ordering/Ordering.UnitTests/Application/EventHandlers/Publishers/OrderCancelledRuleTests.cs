using BuildingBlocks.Messaging.Streams;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers.Rules;

namespace Ordering.UnitTests.Application.EventHandlers.Publishers;

public class OrderCancelledRuleTests
{
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly OrderCancelledRule _rule;

    public OrderCancelledRuleTests()
    {
        var autoMocker = new AutoMocker();
        _orderRepository = autoMocker.GetMock<IOrderRepository>();
        _rule = autoMocker.CreateInstance<OrderCancelledRule>();
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

    // A declined payment: the points the checkout reserved must be released (ADR-0048 §5).
    [Fact]
    public void Match_ReturnsTrue_ForPendingToCancelled() =>
        Assert.True(_rule.Match(Context("MODIFY", "Pending", "Cancelled")));

    // Cancelled after payment: the points already used must be refunded.
    [Fact]
    public void Match_ReturnsTrue_ForCompletedToCancelled() =>
        Assert.True(_rule.Match(Context("MODIFY", "Completed", "Cancelled")));

    // A redelivered record, or a later write to an already-cancelled order, must not give the
    // points back twice.
    [Fact]
    public void Match_ReturnsFalse_ForCancelledToCancelled() =>
        Assert.False(_rule.Match(Context("MODIFY", "Cancelled", "Cancelled")));

    [Fact]
    public void Match_ReturnsFalse_ForPendingToCompleted() =>
        Assert.False(_rule.Match(Context("MODIFY", "Pending", "Completed")));

    [Fact]
    public void Match_ReturnsFalse_ForInsertOfACancelledOrder() =>
        Assert.False(_rule.Match(Context("INSERT", null, "Cancelled")));

    [Fact]
    public void Match_ReturnsFalse_ForRemove() =>
        Assert.False(_rule.Match(Context("REMOVE", "Cancelled", null)));

    [Fact]
    public void Match_ReturnsFalse_WhenImageIsNotAnOrder() =>
        Assert.False(_rule.Match(Context("MODIFY", "Pending", "Cancelled", type: "Coupon")));

    [Fact]
    public async Task BuildAsync_ReturnsOrderCancelledInstruction_WithTheOrderAndCustomer()
    {
        var order = Order.CreateFromCheckout(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of("ORD_1"),
            OrderDataTests.CreateShippingAddressWithVersion("1"),
            OrderDataTests.CreatePaymentWithVersion("1"),
            [(ProductId.Of(Guid.NewGuid()), "Rubber Duck Classic", null, 1, 10m)]);
        order.ApplyPaymentResult(authorized: false);

        _orderRepository
            .Setup(repo => repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var instruction = await _rule.BuildAsync(Context("MODIFY", "Pending", "Cancelled", order.Id.Value));

        Assert.Equal(nameof(OrderCancelledEvent), instruction.DetailType);
        var payload = Assert.IsType<OrderCancelledEvent>(instruction.Payload);
        Assert.Equal(order.Id.Value, payload.OrderId);
        Assert.Equal(order.CustomerId.Value, payload.CustomerId);
    }
}
