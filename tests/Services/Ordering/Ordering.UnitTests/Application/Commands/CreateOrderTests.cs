using BuildingBlocks.Core.Validation;
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
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.DoesNotContain(result, f => f.PropertyName == "OrderName");
        Assert.DoesNotContain(result, f => f.PropertyName == "OrderId");
        Assert.DoesNotContain(result, f => f.PropertyName == "CustomerId");
        Assert.DoesNotContain(result, f => f.PropertyName == "OrderItems");
        Assert.DoesNotContain(result, f => f.PropertyName == "Payment");
        Assert.DoesNotContain(result, f => f.PropertyName == "PaymentMethod");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderIdIsEmpty()
    {
        // Arrange
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems() with
        {
            OrderId = Guid.Empty
        };

        // Act
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.Contains(result, f => f.PropertyName == "OrderId" && f.ErrorMessage == "OrderId is required");
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
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.Contains(result, f => f.PropertyName == "CustomerId" && f.ErrorMessage == "CustomerId is required");
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
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.Contains(result, f => f.PropertyName == "OrderItems" && f.ErrorMessage == "OrderItems should not be empty");
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
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.Contains(result, f => f.PropertyName == "PaymentMethod" && f.ErrorMessage == "Invalid payment method");
    }

    [Fact]
    public void Validator_ShouldNotError_ForCash_EvenWithNoPaymentMethodValidationOnPayment()
    {
        // Arrange
        var baseCommand = CreateOrderCommandTestsDataTests.CreateOrderDtoWithValidItems();
        var command = baseCommand with
        {
            Payment = baseCommand.Payment with { PaymentMethod = PaymentMethod.Cash }
        };

        // Act
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.DoesNotContain(result, f => f.PropertyName == "PaymentMethod");
    }

    [Fact]
    public void Validator_ShouldHaveMultipleErrors_WhenMultiplePropertiesAreInvalid()
    {
        // Arrange
        var command = CreateOrderCommandTestsDataTests.CreateOrderDtoWithInvalidItems();

        // Act
        var result = _validator.Validate(command).ToList();

        // Assert
        Assert.Contains(result, f => f.PropertyName == "OrderId");
        Assert.Contains(result, f => f.PropertyName == "CustomerId");
        Assert.Contains(result, f => f.PropertyName == "OrderItems");
        Assert.Contains(result, f => f.PropertyName == "PaymentMethod");
    }
}
