using Payment.Function;
using Payment.Function.Modules.Payments.Features.VoidPayment;
using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Application.Commands;

// payment-void: the checkout saga's compensation when the cart changed after registration (ADR-0050 §1/§6).
public class VoidPaymentHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository;
    private readonly VoidPaymentHandler _handler;

    public VoidPaymentHandlerTests()
    {
        var autoMocker = new AutoMocker();
        _paymentRepository = autoMocker.GetMock<IPaymentRepository>();
        _paymentRepository
            .Setup(r => r.ReplaceIfStatusAsync(It.IsAny<PaymentEntity>(), PaymentStatus.Registered, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler = autoMocker.CreateInstance<VoidPaymentHandler>();
    }

    private PaymentEntity GivenARegisteredPayment()
    {
        var orderId = Guid.NewGuid();
        var payment = PaymentEntity.Register(
            PaymentId.Of(orderId), orderId, Guid.NewGuid(), 150m, PaymentDataTests.CreateCardDetails());
        _paymentRepository
            .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        return payment;
    }

    [Fact]
    public async Task ShouldVoidARegisteredPayment_OnlyIfStillRegistered()
    {
        var payment = GivenARegisteredPayment();

        var response = await _handler.HandleAsync(new VoidPaymentRequest(payment.OrderId));

        Assert.True(response.Voided);
        _paymentRepository.Verify(r => r.ReplaceIfStatusAsync(
            It.Is<PaymentEntity>(p => p.Id.Value == payment.OrderId && p.Status == PaymentStatus.Voided),
            PaymentStatus.Registered,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldBeIdempotent_TheSecondVoidReturnsFalse()
    {
        var payment = GivenARegisteredPayment();

        await _handler.HandleAsync(new VoidPaymentRequest(payment.OrderId));
        var second = await _handler.HandleAsync(new VoidPaymentRequest(payment.OrderId));

        Assert.False(second.Voided);
        _paymentRepository.Verify(r => r.ReplaceIfStatusAsync(
            It.IsAny<PaymentEntity>(), It.IsAny<PaymentStatus>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldNotVoidAPromotedPayment()
    {
        var payment = GivenARegisteredPayment();
        payment.Promote(awaitingPoints: false);

        var response = await _handler.HandleAsync(new VoidPaymentRequest(payment.OrderId));

        Assert.False(response.Voided);
        _paymentRepository.Verify(r => r.ReplaceIfStatusAsync(
            It.IsAny<PaymentEntity>(), It.IsAny<PaymentStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The checkout event promoted the payment between the read and the write: the promotion wins.
    [Fact]
    public async Task ShouldReportNotVoided_WhenAConcurrentPromotionWins()
    {
        var payment = GivenARegisteredPayment();
        _paymentRepository
            .Setup(r => r.ReplaceIfStatusAsync(It.IsAny<PaymentEntity>(), PaymentStatus.Registered, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var response = await _handler.HandleAsync(new VoidPaymentRequest(payment.OrderId));

        Assert.False(response.Voided);
    }

    [Fact]
    public async Task ShouldReportNotVoided_WhenThereIsNoPayment()
    {
        _paymentRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentEntity?)null);

        var response = await _handler.HandleAsync(new VoidPaymentRequest(Guid.NewGuid()));

        Assert.False(response.Voided);
    }
}
