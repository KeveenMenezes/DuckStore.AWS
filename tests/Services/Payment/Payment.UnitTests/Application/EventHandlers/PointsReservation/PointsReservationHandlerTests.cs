using Payment.Function.Modules.Payments.EventsIntegration.Consumers.PointsReservation;
using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;
using PaymentResultHandler = Payment.Function.Modules.Payments.EventsIntegration.Consumers.PaymentResult.ApplyPaymentResultHandler;

namespace Payment.UnitTests.Application.EventHandlers.PointsReservation;

// Payment waits on Challenges' reservation outcome before asking the gateway (ADR-0048 §6).
public class PointsReservationHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository = new();

    private PaymentEntity AwaitingPayment()
    {
        var payment = PaymentEntity.Create(
            PaymentId.Of(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 190m,
            PaymentDataTests.CreateCardDetails(), awaitingPoints: true);
        _paymentRepository
            .Setup(r => r.GetByOrderIdAsync(payment.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        return payment;
    }

    [Fact]
    public async Task Reserved_ShouldMoveThePaymentToPending()
    {
        var payment = AwaitingPayment();

        var applied = await PointsReservationHandler.ApplyAsync(
            _paymentRepository.Object, new ApplyPointsReservationCommand(payment.OrderId, Reserved: true));

        Assert.Same(payment, applied);
        Assert.Equal(PaymentStatus.Pending, applied.Status);
    }

    [Fact]
    public async Task Failed_ShouldDeclineThePayment_WithInsufficientPoints()
    {
        var payment = AwaitingPayment();

        var applied = await PointsReservationHandler.ApplyAsync(
            _paymentRepository.Object, new ApplyPointsReservationCommand(payment.OrderId, Reserved: false));

        Assert.Equal(PaymentStatus.Declined, applied.Status);
        Assert.Equal("InsufficientPoints", applied.DeclineReason);
    }

    // Payment and Challenges consume BasketCheckoutEvent in parallel: the reservation outcome can
    // arrive before the payment row exists. Throwing makes EventBridge retry (then DLQ) instead of
    // dropping the outcome — a dropped PointsReserved would leave the payment AwaitingPoints forever.
    [Fact]
    public async Task ShouldThrow_WhenThePaymentDoesNotExistYet()
    {
        _paymentRepository
            .Setup(r => r.GetByOrderIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentEntity?)null);

        await Assert.ThrowsAsync<PaymentForOrderNotFoundException>(() => PointsReservationHandler.ApplyAsync(
            _paymentRepository.Object, new ApplyPointsReservationCommand(Guid.NewGuid(), Reserved: true)));
    }

    // The checkout saga registers the payment before the cart closes; the reservation outcome can
    // arrive before the checkout event promotes it. Dropping it would leave the payment
    // AwaitingPoints forever, so it throws and EventBridge retries (ADR-0050 §6).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldThrow_WhileThePaymentIsStillRegistered(bool reserved)
    {
        var orderId = Guid.NewGuid();
        var registered = PaymentEntity.Register(
            PaymentId.Of(orderId), orderId, Guid.NewGuid(), 190m, PaymentDataTests.CreateCardDetails());
        _paymentRepository
            .Setup(r => r.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registered);

        await Assert.ThrowsAsync<PaymentNotYetPromotedException>(() => PointsReservationHandler.ApplyAsync(
            _paymentRepository.Object, new ApplyPointsReservationCommand(orderId, reserved)));
        Assert.Equal(PaymentStatus.Registered, registered.Status);
    }

    [Fact]
    public async Task Reserved_ShouldMoveAPromotedPaymentToPending()
    {
        var orderId = Guid.NewGuid();
        var payment = PaymentEntity.Register(
            PaymentId.Of(orderId), orderId, Guid.NewGuid(), 190m, PaymentDataTests.CreateCardDetails());
        payment.Promote(awaitingPoints: true);
        _paymentRepository
            .Setup(r => r.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var applied = await PointsReservationHandler.ApplyAsync(
            _paymentRepository.Object, new ApplyPointsReservationCommand(orderId, Reserved: true));

        Assert.Equal(PaymentStatus.Pending, applied.Status);
    }

    [Fact]
    public void Mapper_ShouldMapBothOutcomes()
    {
        var orderId = Guid.NewGuid();

        var reserved = PointsReservationMapper.ToCommand(new PointsReservedEvent { OrderId = orderId, OwnerId = "USER#a", Points = 100 });
        var failed = PointsReservationMapper.ToCommand(new PointsReservationFailedEvent { OrderId = orderId, OwnerId = "USER#a", Points = 100 });

        Assert.Equal(new ApplyPointsReservationCommand(orderId, Reserved: true), reserved);
        Assert.Equal(new ApplyPointsReservationCommand(orderId, Reserved: false), failed);
    }

    // The PaymentDeclinedEvent the points rejection publishes also reaches Payment's own
    // PaymentDeclinedConsumer; it must leave the payment exactly as the rejection did.
    [Fact]
    public async Task OwnPaymentDeclinedConsumer_ShouldBeANoOp_OnThePointsRejection()
    {
        var payment = AwaitingPayment();
        payment.RejectPoints();
        _paymentRepository
            .Setup(r => r.GetByIdAsync(payment.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        await new PaymentResultHandler(_paymentRepository.Object).Handle(
            new ApplyPaymentResultCommand(payment.Id.Value, payment.OrderId, Authorized: false, Detail: "SIM-DECLINE"),
            CancellationToken.None);

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal("InsufficientPoints", payment.DeclineReason);
    }
}
