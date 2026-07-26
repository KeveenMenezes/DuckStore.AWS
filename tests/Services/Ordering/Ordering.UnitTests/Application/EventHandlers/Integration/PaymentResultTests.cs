namespace Ordering.UnitTests.Application.EventHandlers.Integration;

public class PaymentResultMapperTests
{
    [Fact]
    public void ToApplyPaymentResultCommand_ShouldMapAuthorized_WhenEventIsPaymentAuthorized()
    {
        // Arrange
        var authorizedEvent = new PaymentAuthorizedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            AuthorizationCode = "AUTH123"
        };

        // Act
        var command = PaymentResultMapper.ToApplyPaymentResultCommand(authorizedEvent);

        // Assert
        Assert.Equal(authorizedEvent.OrderId, command.OrderId);
        Assert.True(command.Authorized);
    }

    [Fact]
    public void ToApplyPaymentResultCommand_ShouldMapDeclined_WhenEventIsPaymentDeclined()
    {
        // Arrange
        var declinedEvent = new PaymentDeclinedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            DeclineReason = "Insufficient funds"
        };

        // Act
        var command = PaymentResultMapper.ToApplyPaymentResultCommand(declinedEvent);

        // Assert
        Assert.Equal(declinedEvent.OrderId, command.OrderId);
        Assert.False(command.Authorized);
    }
}

public class ApplyPaymentResultHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly ApplyPaymentResultHandler _handler;

    public ApplyPaymentResultHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _handler = new ApplyPaymentResultHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldCompleteOrder_WhenPaymentIsAuthorized()
    {
        // Arrange
        var order = OrderDataTests.CreateOrderWithItems();
        var command = new ApplyPaymentResultCommand(order.Id.Value, Authorized: true);

        _orderRepository
            .Setup(repo => repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(OrderStatus.Completed, order.Status);
        _orderRepository.Verify(repo => repo.AddAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldCancelOrder_WhenPaymentIsDeclined()
    {
        // Arrange
        var order = OrderDataTests.CreateOrderWithItems();
        var command = new ApplyPaymentResultCommand(order.Id.Value, Authorized: false);

        _orderRepository
            .Setup(repo => repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        _orderRepository.Verify(repo => repo.AddAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldDoNothing_WhenOrderDoesNotExist()
    {
        // Arrange
        var command = new ApplyPaymentResultCommand(Guid.NewGuid(), Authorized: true);

        _orderRepository
            .Setup(repo => repo.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _orderRepository.Verify(
            repo => repo.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldNotChangeStatus_WhenOrderIsAlreadyCompleted()
    {
        // Arrange
        var order = OrderDataTests.CreateOrderWithItems();
        order.ApplyPaymentResult(authorized: true);
        var command = new ApplyPaymentResultCommand(order.Id.Value, Authorized: false);

        _orderRepository
            .Setup(repo => repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(OrderStatus.Completed, order.Status);
    }
}
