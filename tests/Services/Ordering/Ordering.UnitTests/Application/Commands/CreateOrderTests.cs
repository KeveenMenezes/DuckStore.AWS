using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

namespace Ordering.UnitTests.Application.Commands;

public class CreateOrderTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly CreateOrderCommandValidator _validator;
    private readonly CreateOrderHandler _handler;

    public CreateOrderTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _validator = new CreateOrderCommandValidator();
        _handler = new CreateOrderHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldCreateOrderSuccessfully()
    {
        // Arrange
        var command = new CreateOrderCommand(OrderDtoDataTests.CreateOrderDtoWithValidItems());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        _orderRepository.Verify(repo =>
            repo.AddAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Handle_ShouldNotError_WhenOrderDtoCorrectValues()
    {
        // Arrange
        var command = new CreateOrderCommand(
            OrderDtoDataTests.CreateOrderDtoWithValidItems());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Order.OrderName);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.CustomerId);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.OrderItems);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.Payment);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.Payment.CardNumber);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.Payment.PaymentMethod);
        result.ShouldNotHaveValidationErrorFor(x => x.Order.Status);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderDtoInvalidItems()
    {
        // Arrange
        var command = new CreateOrderCommand(
            OrderDtoDataTests.CreateOrderDtoWithInvalidItems());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Order.OrderName)
            .WithErrorMessage("Name is required");

        result.ShouldHaveValidationErrorFor(x => x.Order.CustomerId)
            .WithErrorMessage("CustomerId is required");

        result.ShouldHaveValidationErrorFor(x => x.Order.OrderItems)
            .WithErrorMessage("OrderItems should not be empty");

        result.ShouldHaveValidationErrorFor(x => x.Order.Payment.CardNumber)
            .WithErrorMessage("Invalid card number");

        result.ShouldHaveValidationErrorFor(x => x.Order.Payment.PaymentMethod)
            .WithErrorMessage("Invalid payment method");

        result.ShouldHaveValidationErrorFor(x => x.Order.Status)
            .WithErrorMessage("Invalid order status");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderDtoInvalidPayment()
    {
        //Arrange
        var command = new CreateOrderCommand(
            OrderDtoDataTests.CreateOrderDtoWithInvalidItems());

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        command = new CreateOrderCommand(
            command.Order with { Payment = null });
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Order.Payment)
            .WithErrorMessage("Payment is required");
    }
}
