namespace Ordering.UnitTests.Application.Commands;

public class DeleteOrderTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly DeleteOrderCommandValidator _validator;
    private readonly DeleteOrderHandler _handler;

    public DeleteOrderTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _validator = new DeleteOrderCommandValidator();
        _handler = new DeleteOrderHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldDeleteOrderSuccessfully()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = new Mock<Order>();
        _orderRepository.Setup(
            repo =>
                repo.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order.Object);

        var command = new DeleteOrderCommand(orderId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsDeleted);
        _orderRepository.Verify(
            repo =>
                repo.Delete(order.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenOrderNotFound()
    {
        // Arrange
        var orderId = Guid.NewGuid();

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        _orderRepository.Setup(
            repo =>
                repo.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order)null);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

        var command = new DeleteOrderCommand(orderId);

        // Act & Assert
        await Assert.ThrowsAsync<OrderNotFoundBadRequestException>(() => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderIdIsEmpty()
    {
        // Arrange
        var command = new DeleteOrderCommand(Guid.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.OrderId)
            .WithErrorMessage("Order Id is required.");
    }

    [Fact]
    public void Validator_ShouldNotHaveError_WhenOrderIdIsValid()
    {
        // Arrange
        var command = new DeleteOrderCommand(Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.OrderId);
    }
}
