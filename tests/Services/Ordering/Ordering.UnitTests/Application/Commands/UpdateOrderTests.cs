using Ordering.Application.Orders.Commands.UpdateOrder;

namespace Ordering.UnitTests.Application.Commands;

public class UpdateOrderTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly UpdateOrderCommandValidator _validator;
    private readonly UpdateOrderHandler _handler;

    public UpdateOrderTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _validator = new UpdateOrderCommandValidator();
        _handler = new UpdateOrderHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldUpdateOrderSuccessfully()
    {
        // Arrange
        var order = OrderDataTests.CreateOrderWithItems();

        _orderRepository.Setup(
            repo =>
                repo.GetByIdAsync(order.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var command = new UpdateOrderCommand(
            OrderDtoDataTests.CreateOrderDtoWithValidItems(order.Id.Value));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _orderRepository.Verify(
            repo =>
                repo.Update(order, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenOrderNotFound()
    {
        // Arrange
        var command = new UpdateOrderCommand(OrderDtoDataTests.CreateOrderDtoWithValidItems());

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        _orderRepository.Setup(
            repo =>
                repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order)null);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

        // Act & Assert
        await Assert.ThrowsAsync<OrderNotFoundBadRequestException>(() => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldNotHaveError_WhenValidCommand()
    {
        // Arrange
        var command = new UpdateOrderCommand(OrderDtoDataTests.CreateOrderDtoWithValidItems());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Order.Id);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.OrderName);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.CustomerId);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenInvalidCommand()
    {
        // Arrange
        var command = new UpdateOrderCommand(
            OrderDtoDataTests.CreateOrderDtoWithInvalidItems());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Order.Id)
            .WithErrorMessage("Id is required");

        result.ShouldHaveValidationErrorFor(x => x.Order.OrderName)
            .WithErrorMessage("Name is required");

        result.ShouldHaveValidationErrorFor(x => x.Order.CustomerId)
            .WithErrorMessage("CustomerId is required");
    }
}
