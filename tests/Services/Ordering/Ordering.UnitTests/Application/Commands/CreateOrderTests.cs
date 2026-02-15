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
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems();

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
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.OrderName);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomerId);
        result.ShouldNotHaveValidationErrorFor(x => x.OrderItems);
        result.ShouldNotHaveValidationErrorFor(x => x.Payment);
        result.ShouldNotHaveValidationErrorFor(x => x.Payment.CardNumber);
        result.ShouldNotHaveValidationErrorFor(x => x.Payment.PaymentMethod);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCustomerIdIsEmpty()
    {
        // Arrange
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems() with
        {
            CustomerId = Guid.Empty
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CustomerId)
            .WithErrorMessage("CustomerId is required");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderItemsIsEmpty()
    {
        // Arrange
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems() with
        {
            OrderItems = []
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.OrderItems)
            .WithErrorMessage("OrderItems should not be empty");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCardNumberIsInvalid()
    {
        // Arrange
        var baseCommand = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems();
        var command = baseCommand with
        {
            Payment = baseCommand.Payment with { CardNumber = "invalid" }
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Payment.CardNumber)
            .WithErrorMessage("Invalid card number");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenPaymentMethodIsInvalid()
    {
        // Arrange
        var baseCommand = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems();
        var command = baseCommand with
        {
            Payment = baseCommand.Payment with { PaymentMethod = 0 }
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Payment.PaymentMethod)
            .WithErrorMessage("Invalid payment method");
    }

    [Fact]
    public void Validator_ShouldHaveMultipleErrors_WhenMultiplePropertiesAreInvalid()
    {
        // Arrange
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithInvalidItems();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CustomerId);
        result.ShouldHaveValidationErrorFor(x => x.OrderItems);
        result.ShouldHaveValidationErrorFor(x => x.Payment.CardNumber);
        result.ShouldHaveValidationErrorFor(x => x.Payment.PaymentMethod);
    }
}
