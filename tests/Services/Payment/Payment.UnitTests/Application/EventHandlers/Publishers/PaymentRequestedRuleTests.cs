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

    private static StreamContext<PaymentStreamImage> Context(
        string eventName, string? newType = "Payment", string? newStatus = "Pending", Guid? id = null) =>
        new(eventName, null,
            newType is null ? null : new PaymentStreamImage(id ?? Guid.NewGuid(), newType, newStatus ?? string.Empty));

    [Fact]
    public void Match_ReturnsTrue_ForInsertOfPendingPayment() =>
        Assert.True(_rule.Match(Context("INSERT")));

    [Theory]
    [InlineData("MODIFY")]
    [InlineData("REMOVE")]
    public void Match_ReturnsFalse_ForNonInsertEvents(string eventName) =>
        Assert.False(_rule.Match(Context(eventName)));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsNotAPayment() =>
        Assert.False(_rule.Match(Context("INSERT", newType: "Order")));

    [Fact]
    public void Match_ReturnsFalse_WhenPaymentIsNotPending() =>
        Assert.False(_rule.Match(Context("INSERT", newStatus: "Authorized")));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsMissing() =>
        Assert.False(_rule.Match(Context("INSERT", newType: null)));

    [Fact]
    public async Task BuildAsync_ReturnsPaymentRequestedInstruction_ForTheRehydratedPayment()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        _paymentRepository
            .Setup(repo => repo.GetByIdAsync(payment.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var instruction = await _rule.BuildAsync(Context("INSERT", id: payment.Id.Value));

        Assert.Equal(nameof(PaymentRequestedEvent), instruction.DetailType);
        var payload = Assert.IsType<PaymentRequestedEvent>(instruction.Payload);
        Assert.Equal(payment.Id.Value, payload.PaymentId);
        Assert.Equal(payment.OrderId, payload.OrderId);
    }
}
