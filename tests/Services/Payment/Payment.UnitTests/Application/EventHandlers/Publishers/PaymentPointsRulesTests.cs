using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Application.EventHandlers.Publishers;

// The stream rules around AwaitingPoints (ADR-0048 §6): PaymentRequested waits for the points, and a
// points rejection is announced with the existing PaymentDeclinedEvent so Ordering cancels the order.
public class PaymentPointsRulesTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository;
    private readonly PaymentRequestedRule _requested;
    private readonly PaymentDeclinedInsufficientPointsRule _declined;

    public PaymentPointsRulesTests()
    {
        var autoMocker = new AutoMocker();
        _paymentRepository = autoMocker.GetMock<IPaymentRepository>();
        _requested = autoMocker.CreateInstance<PaymentRequestedRule>();
        _declined = autoMocker.CreateInstance<PaymentDeclinedInsufficientPointsRule>();
    }

    private static StreamContext<PaymentStreamImage> Context(string eventName, string? oldStatus, string? newStatus, Guid? id = null)
    {
        var paymentId = id ?? Guid.NewGuid();
        return new(
            eventName,
            oldStatus is null ? null : new PaymentStreamImage(paymentId, "Payment", oldStatus),
            newStatus is null ? null : new PaymentStreamImage(paymentId, "Payment", newStatus));
    }

    [Fact]
    public void Requested_ShouldNotMatch_TheInsertOfAPaymentAwaitingPoints() =>
        Assert.False(_requested.Match(Context("INSERT", null, "AwaitingPoints")));

    [Fact]
    public void Requested_ShouldMatch_OnceThePointsAreConfirmed() =>
        Assert.True(_requested.Match(Context("MODIFY", "AwaitingPoints", "Pending")));

    // The gateway's result is a MODIFY Pending -> Authorized/Declined; it must not request again.
    [Theory]
    [InlineData("Pending", "Authorized")]
    [InlineData("Pending", "Declined")]
    [InlineData("Pending", "Pending")]
    public void Requested_ShouldNotMatch_OtherModifies(string oldStatus, string newStatus) =>
        Assert.False(_requested.Match(Context("MODIFY", oldStatus, newStatus)));

    [Fact]
    public void Requested_ShouldStillMatch_TheInsertOfAPendingPayment() =>
        Assert.True(_requested.Match(Context("INSERT", null, "Pending")));

    [Fact]
    public void Declined_ShouldMatch_TheRejectionOfThePoints() =>
        Assert.True(_declined.Match(Context("MODIFY", "AwaitingPoints", "Declined")));

    // A gateway decline is already published by PaymentGateway; publishing it again would be a
    // second PaymentDeclinedEvent for the same payment.
    [Fact]
    public void Declined_ShouldNotMatch_AGatewayDecline() =>
        Assert.False(_declined.Match(Context("MODIFY", "Pending", "Declined")));

    [Fact]
    public void Declined_ShouldNotMatch_ARewriteOfADeclinedPayment() =>
        Assert.False(_declined.Match(Context("MODIFY", "Declined", "Declined")));

    [Fact]
    public async Task Declined_ShouldPublishPaymentDeclined_WithTheOrderAndReason()
    {
        var payment = PaymentEntity.Create(
            PaymentId.Of(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 190m,
            PaymentDataTests.CreateCardDetails(), awaitingPoints: true);
        payment.RejectPoints();
        _paymentRepository
            .Setup(r => r.GetByIdAsync(payment.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var instruction = await _declined.BuildAsync(Context("MODIFY", "AwaitingPoints", "Declined", payment.Id.Value));

        Assert.Equal(nameof(PaymentDeclinedEvent), instruction.DetailType);
        var payload = Assert.IsType<PaymentDeclinedEvent>(instruction.Payload);
        Assert.Equal(payment.Id.Value, payload.PaymentId);
        Assert.Equal(payment.OrderId, payload.OrderId);
        Assert.Equal("InsufficientPoints", payload.DeclineReason);
    }
}
