using Ordering.Domain.AggregatesModel.OrderAggregate.Events;

namespace Ordering.UnitTests.Application.EventHandlers.Domain;

public class OrderCreateEventHandlerTests
{
    private readonly Mock<ILogger<OrderCreateEventHandler>> _loggerMock;
    private readonly OrderCreateEventHandler _handler;

    public OrderCreateEventHandlerTests()
    {
        _loggerMock = new Mock<ILogger<OrderCreateEventHandler>>();
        _handler = new OrderCreateEventHandler(_loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldLogDomainEvent()
    {
        // Arrange
        var domainEvent = new OrderCreatedEvent(
            OrderDataTests.CreateOrderWithItems());

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _loggerMock.VerifyLog(
            LogLevel.Information,
            "OrderCreatedEvent");
    }
}