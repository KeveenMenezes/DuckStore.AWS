using BuildingBlocks.Messaging.Streams;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers.Rules;

namespace Ordering.UnitTests.Application.EventHandlers.Publishers;

public class OrderCreatedRuleTests
{
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly OrderCreatedRule _rule;

    public OrderCreatedRuleTests()
    {
        var autoMocker = new AutoMocker();
        _orderRepository = autoMocker.GetMock<IOrderRepository>();
        _rule = autoMocker.CreateInstance<OrderCreatedRule>();
    }

    private static StreamContext<OrderStreamImage> Context(
        string eventName, string? newType = "Order", Guid? id = null) =>
        new(eventName, null,
            newType is null ? null : new OrderStreamImage(id ?? Guid.NewGuid(), newType, "Pending"));

    [Fact]
    public void Match_ReturnsTrue_ForInsertOfOrder() =>
        Assert.True(_rule.Match(Context("INSERT")));

    [Theory]
    [InlineData("MODIFY")]
    [InlineData("REMOVE")]
    public void Match_ReturnsFalse_ForNonInsertEvents(string eventName) =>
        Assert.False(_rule.Match(Context(eventName)));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsNotAnOrder() =>
        Assert.False(_rule.Match(Context("INSERT", newType: "Coupon")));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsMissing() =>
        Assert.False(_rule.Match(Context("INSERT", newType: null)));

    [Fact]
    public async Task BuildAsync_ReturnsOrderCreatedInstruction_ForTheRehydratedOrder()
    {
        var order = OrderDataTests.CreateOrderWithItems("1");
        _orderRepository
            .Setup(repo => repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var instruction = await _rule.BuildAsync(Context("INSERT", id: order.Id.Value));

        Assert.Equal(nameof(OrderCreatedEvent), instruction.DetailType);
        var payload = Assert.IsType<OrderCreatedEvent>(instruction.Payload);
        Assert.Equal(order.Id.Value, payload.OrderId);
    }
}
