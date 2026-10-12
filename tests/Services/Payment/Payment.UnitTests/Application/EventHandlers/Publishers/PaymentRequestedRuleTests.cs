namespace Payment.UnitTests.Application.EventHandlers.Publishers;

public class PaymentRequestedRuleTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository;
    private readonly PaymentRequestedRule _rule;

    public PaymentRequestedRuleTests()
    {
        var autoMocker = new AutoMocker();
        _paymentRepository = autoMocker.GetMock<IPaymentRepository>();
        _rule = autoMocker.CreateInstance<PaymentRequestedRule>();
    }

    // Every payment is registered by the checkout saga and promoted by the checkout event, so the
    // gateway is asked on the MODIFY Registered → Pending (ADR-0050 §6).
    private static StreamContext<PaymentStreamImage> Context(
        string eventName, string? newType = "Payment", string? newStatus = "Pending", Guid? id = null,
        string? oldStatus = "Registered")
    {
        var paymentId = id ?? Guid.NewGuid();
        return new(eventName,
            oldStatus is null ? null : new PaymentStreamImage(paymentId, "Payment", oldStatus),
            newType is null ? null : new PaymentStreamImage(paymentId, newType, newStatus ?? string.Empty));
    }

    [Fact]
    public void Match_ReturnsTrue_ForThePromotionOfARegisteredPayment() =>
        Assert.True(_rule.Match(Context("MODIFY")));

    // Only the old checkout pipeline inserted Pending payments.
    [Fact]
    public void Match_ReturnsFalse_ForTheInsertOfAPendingPayment() =>
        Assert.False(_rule.Match(Context("INSERT", oldStatus: null)));

    [Fact]
    public void Match_ReturnsFalse_ForRemoveEvents() =>
        Assert.False(_rule.Match(Context("REMOVE")));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsNotAPayment() =>
        Assert.False(_rule.Match(Context("MODIFY", newType: "Order")));

    [Fact]
    public void Match_ReturnsFalse_WhenPaymentIsNotPending() =>
        Assert.False(_rule.Match(Context("MODIFY", newStatus: "Authorized")));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsMissing() =>
        Assert.False(_rule.Match(Context("MODIFY", newType: null)));

    [Fact]
    public async Task BuildAsync_ReturnsPaymentRequestedInstruction_ForTheRehydratedPayment()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        _paymentRepository
            .Setup(repo => repo.GetByIdAsync(payment.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var instruction = await _rule.BuildAsync(Context("MODIFY", id: payment.Id.Value));

        Assert.Equal(nameof(PaymentRequestedEvent), instruction.DetailType);
        var payload = Assert.IsType<PaymentRequestedEvent>(instruction.Payload);
        Assert.Equal(payment.Id.Value, payload.PaymentId);
        Assert.Equal(payment.OrderId, payload.OrderId);
    }
}
