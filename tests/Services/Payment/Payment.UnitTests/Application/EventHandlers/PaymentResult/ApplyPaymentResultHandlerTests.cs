using PaymentResultHandler = Payment.Function.Modules.Payments.EventsIntegration.Consumers.PaymentResult.ApplyPaymentResultHandler;

namespace Payment.UnitTests.Application.EventHandlers.PaymentResult;

public class ApplyPaymentResultHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository;
    private readonly PaymentResultHandler _handler;

    public ApplyPaymentResultHandlerTests()
    {
        var autoMocker = new AutoMocker();
        _paymentRepository = autoMocker.GetMock<IPaymentRepository>();
        _handler = new PaymentResultHandler(_paymentRepository.Object);
    }

    [Fact]
    public async Task Handle_AuthorizesPayment_WhenAuthorizedIsTrue()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        _paymentRepository
            .Setup(repo => repo.GetByIdAsync(payment.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var command = new ApplyPaymentResultCommand(payment.Id.Value, payment.OrderId, Authorized: true, Detail: "SIM-123");

        await _handler.Handle(command, CancellationToken.None);

        _paymentRepository.Verify(repo =>
            repo.AddAsync(
                It.Is<Payment.Function.Modules.Payments.Domain.Entities.Payment>(p =>
                    p.Status == PaymentStatus.Authorized && p.AuthorizationCode == "SIM-123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DeclinesPayment_WhenAuthorizedIsFalse()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        _paymentRepository
            .Setup(repo => repo.GetByIdAsync(payment.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var command = new ApplyPaymentResultCommand(payment.Id.Value, payment.OrderId, Authorized: false, Detail: "card_declined");

        await _handler.Handle(command, CancellationToken.None);

        _paymentRepository.Verify(repo =>
            repo.AddAsync(
                It.Is<Payment.Function.Modules.Payments.Domain.Entities.Payment>(p =>
                    p.Status == PaymentStatus.Declined && p.DeclineReason == "card_declined"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DoesNotPersist_WhenPaymentDoesNotExist()
    {
        _paymentRepository
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment.Function.Modules.Payments.Domain.Entities.Payment?)null);

        var command = new ApplyPaymentResultCommand(Guid.NewGuid(), Guid.NewGuid(), Authorized: true, Detail: "SIM-123");

        await _handler.Handle(command, CancellationToken.None);

        _paymentRepository.Verify(repo =>
            repo.AddAsync(It.IsAny<Payment.Function.Modules.Payments.Domain.Entities.Payment>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
