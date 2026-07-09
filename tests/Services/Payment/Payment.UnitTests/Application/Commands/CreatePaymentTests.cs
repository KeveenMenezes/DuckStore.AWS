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
    public void Validator_ShouldNotError_WhenCommandIsValid()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(x => x.OrderId);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomerId);
        result.ShouldNotHaveValidationErrorFor(x => x.Amount);
        result.ShouldNotHaveValidationErrorFor(x => x.CardNumber);
        result.ShouldNotHaveValidationErrorFor(x => x.PaymentMethod);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOrderIdIsEmpty()
    {
        var command = ValidCommand() with { OrderId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.OrderId)
            .WithErrorMessage("OrderId is required");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenAmountIsZero()
    {
        var command = ValidCommand() with { Amount = 0 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Amount)
            .WithErrorMessage("Amount must be greater than zero");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCardNumberIsInvalid()
    {
        var command = ValidCommand() with { CardNumber = "invalid" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CardNumber)
            .WithErrorMessage("Invalid card number");
    }

    [Fact]
    public void Validator_ShouldNotError_ForCash_EvenWithEmptyCardNumber()
    {
        var command = ValidCommand() with { CardNumber = "", Cvv = "", PaymentMethod = PaymentMethod.Cash };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.CardNumber);
    }
}
