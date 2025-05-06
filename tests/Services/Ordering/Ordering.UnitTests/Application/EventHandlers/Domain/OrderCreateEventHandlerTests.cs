using Microsoft.FeatureManagement;
using Ordering.Domain.AggregatesModel.OrderAggregate.Events;

namespace Ordering.UnitTests.Application.EventHandlers.Domain;

public class OrderCreateEventHandlerTests
{
    private readonly Mock<IPublishEndpoint> _publishEndpointMock;
    private readonly Mock<ILogger<OrderCreateEventHandler>> _loggerMock;
    private readonly Mock<IFeatureManager> _featureManagerMock;
    private readonly OrderCreateEventHandler _handler;

    public OrderCreateEventHandlerTests()
    {
        _publishEndpointMock = new Mock<IPublishEndpoint>();
        _loggerMock = new Mock<ILogger<OrderCreateEventHandler>>();
        _featureManagerMock = new Mock<IFeatureManager>();

        _handler = new OrderCreateEventHandler(
            _publishEndpointMock.Object,
            _loggerMock.Object,
            _featureManagerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldPublishEvent_WhenFeatureEnabled()
    {
        // Arrange
        var domainEvent = new OrderCreatedEvent(
            OrderDataTests.CreateOrderWithItems());

        _featureManagerMock
            .Setup(fm => fm.IsEnabledAsync("OrderFullfilment"))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _publishEndpointMock.Verify(
            pe =>
                pe.Publish(
                    It.IsAny<OrderDto>(),
                    It.IsAny<CancellationToken>()),
            Times.Once);

        _loggerMock.VerifyLog(
            LogLevel.Information,
            "Domain Event handled: OrderCreatedEvent");
    }

    [Fact]
    public async Task Handle_ShouldNotPublishEvent_WhenFeatureDisabled()
    {
        // Arrange
        var domainEvent = new OrderCreatedEvent(
            OrderDataTests.CreateOrderWithItems());

        _featureManagerMock
            .Setup(fm => fm.IsEnabledAsync("OrderFullfilment"))
            .ReturnsAsync(false);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _publishEndpointMock.Verify(
            pe =>
                pe.Publish(
                    It.IsAny<OrderDto>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);

        _loggerMock.VerifyLog(
            LogLevel.Information,
            "Domain Event handled: OrderCreatedEvent");
    }
}
