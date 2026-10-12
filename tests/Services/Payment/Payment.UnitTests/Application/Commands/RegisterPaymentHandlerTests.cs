using Payment.Function;
using Payment.Function.Modules.Payments.Features.RegisterPayment;
using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Application.Commands;

// payment-register: the checkout saga's RegisterPayment step (ADR-0050 §6/§10). Refusals come back as
// data so the saga can map them to its errorType.
public class RegisterPaymentHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository;
    private readonly RegisterPaymentHandler _handler;

    public RegisterPaymentHandlerTests()
    {
        var autoMocker = new AutoMocker();
        autoMocker.Use<TimeProvider>(FixedClock.October2026);
        _paymentRepository = autoMocker.GetMock<IPaymentRepository>();
        _paymentRepository
            .Setup(r => r.TryAddAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler = autoMocker.CreateInstance<RegisterPaymentHandler>();
    }

    private static RegisterPaymentRequest Request(
        decimal amount = 150m, int method = 2, string? number = "4111111111111111",
        string? expiration = "12/2030", string? cvv = "123", Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(), Guid.NewGuid(), amount, method, Installments: 3,
            CardName: "Ana Silva", CardNumber: number, Expiration: expiration, Cvv: cvv);

    private void GivenAnExistingPayment(PaymentEntity existing)
    {
        _paymentRepository
            .Setup(r => r.TryAddAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _paymentRepository
            .Setup(r => r.GetByIdAsync(existing.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
    }

    private static PaymentEntity RegisteredFor(RegisterPaymentRequest request) =>
        PaymentEntity.Register(
            PaymentId.Of(request.OrderId), request.OrderId, request.CustomerId, request.Amount,
            CardDetails.Of(request.CardNumber!, request.Expiration!, request.Cvv!, (PaymentMethod)request.PaymentMethod));

    [Fact]
    public async Task ShouldRegisterThePayment_KeyedByTheOrder()
    {
        var request = Request();

        var response = await _handler.HandleAsync(request);

        Assert.Equal(request.OrderId, response.PaymentId);
        Assert.Null(response.Refusal);
        _paymentRepository.Verify(r => r.TryAddAsync(
            It.Is<PaymentEntity>(p =>
                p.Id.Value == request.OrderId
                && p.OrderId == request.OrderId
                && p.CustomerId == request.CustomerId
                && p.Amount == 150m
                && p.Status == PaymentStatus.Registered
                && p.Card.PaymentMethod == PaymentMethod.Credit),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("4111111111111112", "12/2030", "123", "CardNumber")]
    [InlineData("4111111111111111", "09/2026", "123", "Expiration")]
    [InlineData("4111111111111111", "12/30", "123", "Expiration")]
    [InlineData("4111111111111111", "12/2030", "12", "Cvv")]
    [InlineData(null, "12/2030", "123", "CardNumber")]
    public async Task ShouldRefuseAnInvalidCard_WithTheField_AndWriteNothing(
        string? number, string expiration, string cvv, string field)
    {
        var response = await _handler.HandleAsync(Request(number: number, expiration: expiration, cvv: cvv));

        Assert.Null(response.PaymentId);
        Assert.Equal(PaymentRefusals.InvalidCard, response.Refusal!.ErrorType);
        Assert.Equal(field, response.Refusal.Field);
        Assert.False(string.IsNullOrWhiteSpace(response.Refusal.Message));
        _paymentRepository.Verify(
            r => r.TryAddAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShouldRegisterCash_WithoutACard()
    {
        var request = Request(method: 3, number: null, expiration: null, cvv: null);

        var response = await _handler.HandleAsync(request);

        Assert.Equal(request.OrderId, response.PaymentId);
        _paymentRepository.Verify(r => r.TryAddAsync(
            It.Is<PaymentEntity>(p => p.Card.PaymentMethod == PaymentMethod.Cash && p.Card.CardNumber == ""),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // The saga retried the same checkout: the payment it registered the first time is the answer.
    [Fact]
    public async Task ShouldReturnTheSamePayment_WhenTheSameRegistrationIsReplayed()
    {
        var request = Request();
        GivenAnExistingPayment(RegisteredFor(request));

        var response = await _handler.HandleAsync(request);

        Assert.Equal(request.OrderId, response.PaymentId);
        Assert.Null(response.Refusal);
    }

    public static TheoryData<string> NoLongerReplayable => ["voided", "promoted", "different amount", "different method"];

    [Theory]
    [MemberData(nameof(NoLongerReplayable))]
    public async Task ShouldRequireANewCheckout_WhenTheStoredPaymentCannotBeReplayed(string scenario)
    {
        var request = Request();
        var existing = RegisteredFor(request);
        switch (scenario)
        {
            case "voided": existing.Void(); break;
            case "promoted": existing.Promote(awaitingPoints: false); break;
            case "different amount": request = request with { Amount = 151m }; break;
            case "different method": request = request with { PaymentMethod = 1 }; break;
        }
        GivenAnExistingPayment(existing);

        var response = await _handler.HandleAsync(request);

        Assert.Null(response.PaymentId);
        Assert.Equal(PaymentRefusals.CheckoutRetryRequired, response.Refusal!.ErrorType);
        Assert.Null(response.Refusal.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task ShouldThrow_OnANonPositiveAmount(decimal amount) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _handler.HandleAsync(Request(amount: amount)));

    [Fact]
    public async Task ShouldThrow_OnAnUnknownMethod() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _handler.HandleAsync(Request(method: 7)));

    [Fact]
    public async Task ShouldThrow_WithoutAnOrder() =>
        await Assert.ThrowsAnyAsync<Exception>(() => _handler.HandleAsync(Request(orderId: Guid.Empty)));
}
