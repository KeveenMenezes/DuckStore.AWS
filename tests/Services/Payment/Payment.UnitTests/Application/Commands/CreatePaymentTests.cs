using BuildingBlocks.Core.Validation;
namespace Payment.UnitTests.Application.Commands;

public class CreatePaymentTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository;
    private readonly CreatePaymentCommandValidator _validator;
    private readonly CreatePaymentHandler _handler;

    public CreatePaymentTests()
    {
        var autoMocker = new AutoMocker();
        _paymentRepository = autoMocker.GetMock<IPaymentRepository>();
        _validator = new CreatePaymentCommandValidator();
        _handler = new CreatePaymentHandler(_paymentRepository.Object);
    }

    private static CreatePaymentCommand ValidCommand(Guid? orderId = null) =>
        new(
            OrderId: orderId ?? Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            Amount: 1400m,
            CardNumber: "4111111111111111",
            Expiration: "12/28",
            Cvv: "123",
            PaymentMethod: PaymentMethod.Credit);

    [Fact]
    public async Task Handle_ShouldCreatePendingPaymentSuccessfully()
    {
        var command = ValidCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        _paymentRepository.Verify(repo =>
            repo.AddAsync(
                It.Is<Payment.Function.Modules.Payments.Domain.Entities.Payment>(p => p.Status == PaymentStatus.Pending),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldCarryTheDiscountId_ThroughToTheCreatedPayment()
    {
        var command = ValidCommand() with { DiscountId = "discount-1" };
        Payment.Function.Modules.Payments.Domain.Entities.Payment? captured = null;
        _paymentRepository
            .Setup(repo => repo.AddAsync(
                It.IsAny<Payment.Function.Modules.Payments.Domain.Entities.Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment.Function.Modules.Payments.Domain.Entities.Payment, CancellationToken>(
                (p, _) => captured = p)
            .Returns(Task.CompletedTask);

        await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("discount-1", captured?.DiscountId);
    }

    [Fact]
    public void Validator_ShouldNotError_WhenCommandIsValid()
    {
        var result = _validator.Validate(ValidCommand()).ToList();

        Assert.DoesNotContain(result, f => f.PropertyName == "OrderId");
        Assert.DoesNotContain(result, f => f.PropertyName == "CustomerId");
        Assert.DoesNotContain(result, f => f.PropertyName == "Amount");
        Assert.DoesNotContain(result, f => f.PropertyName == "CardNumber");
        Assert.DoesNotContain(result, f => f.PropertyName == "PaymentMethod");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderIdIsEmpty()
    {
        var command = ValidCommand() with { OrderId = Guid.Empty };

        var result = _validator.Validate(command).ToList();

        Assert.Contains(result, f => f.PropertyName == "OrderId" && f.ErrorMessage == "OrderId is required");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenAmountIsZero()
    {
        var command = ValidCommand() with { Amount = 0 };

        var result = _validator.Validate(command).ToList();

        Assert.Contains(result, f => f.PropertyName == "Amount" && f.ErrorMessage == "Amount must be greater than zero");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCardNumberIsInvalid()
    {
        var command = ValidCommand() with { CardNumber = "invalid" };

        var result = _validator.Validate(command).ToList();

        Assert.Contains(result, f => f.PropertyName == "CardNumber" && f.ErrorMessage == "Invalid card number");
    }

    [Fact]
    public void Validator_ShouldNotError_ForCash_EvenWithEmptyCardNumber()
    {
        var command = ValidCommand() with { CardNumber = "", Cvv = "", PaymentMethod = PaymentMethod.Cash };

        var result = _validator.Validate(command).ToList();

        Assert.DoesNotContain(result, f => f.PropertyName == "CardNumber");
    }
}
