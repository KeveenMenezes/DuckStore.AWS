namespace Ordering.UnitTests.Application.EventHandlers.Integration;

public class BasketCheckoutEventTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<ISender> _senderMock;
    private readonly Mock<ILogger<BasketCheckoutEventHandler>> _loggerMock;
    private readonly Mock<ConsumeContext<BasketCheckoutEvent>> _contextMock;
    private readonly BasketCheckoutEventHandler _handler;

    public BasketCheckoutEventTests()
    {
        _autoMocker = new AutoMocker();
        _senderMock = _autoMocker.GetMock<ISender>();
        _loggerMock = _autoMocker.GetMock<ILogger<BasketCheckoutEventHandler>>();
        _contextMock = _autoMocker.GetMock<ConsumeContext<BasketCheckoutEvent>>();
        _handler = new BasketCheckoutEventHandler(_senderMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ShouldSendCreateOrderCommand_WhenEventIsValid()
    {
        // Arrange
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent();
        _contextMock.Setup(x => x.Message).Returns(basketCheckoutEvent);
        _contextMock.Setup(x => x.CancellationToken).Returns(CancellationToken.None);

        // Act
        await _handler.Consume(_contextMock.Object);

        // Assert
        _senderMock.Verify(
            sender => sender.Send(
                It.Is<CreateOrderCommand>(command =>
                    command.Order.CustomerId == basketCheckoutEvent.CustomerId &&
                    command.Order.OrderName == basketCheckoutEvent.UserName),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _loggerMock.VerifyLog(
            LogLevel.Information,
            "Integration Event handled: BasketCheckoutEvent");
    }
}
