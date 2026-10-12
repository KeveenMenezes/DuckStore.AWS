namespace Payment.UnitTests.Application.EventHandlers.Publishers;

// A Registered payment never reaches the gateway; its promotion to Pending does (ADR-0050 §6).
public class PaymentRegistrationRulesTests
{
    private readonly PaymentRequestedRule _requested;
    private readonly PaymentDeclinedInsufficientPointsRule _declined;

    public PaymentRegistrationRulesTests()
    {
        var autoMocker = new AutoMocker();
        _requested = autoMocker.CreateInstance<PaymentRequestedRule>();
        _declined = autoMocker.CreateInstance<PaymentDeclinedInsufficientPointsRule>();
    }

    private static StreamContext<PaymentStreamImage> Context(string eventName, string? oldStatus, string newStatus)
    {
        var id = Guid.NewGuid();
        return new(
            eventName,
            oldStatus is null ? null : new PaymentStreamImage(id, "Payment", oldStatus),
            new PaymentStreamImage(id, "Payment", newStatus));
    }

    [Fact]
    public void Requested_ShouldMatch_ThePromotionOfARegisteredPayment() =>
        Assert.True(_requested.Match(Context("MODIFY", "Registered", "Pending")));

    public static TheoryData<string, string?, string> NeverRequested => new()
    {
        { "INSERT", null, "Registered" },
        { "MODIFY", "Registered", "AwaitingPoints" },
        { "MODIFY", "Registered", "Voided" },
        { "MODIFY", "Voided", "Voided" },
        { "INSERT", null, "Voided" },
    };

    [Theory]
    [MemberData(nameof(NeverRequested))]
    public void Requested_ShouldNotMatch_RegistrationOrVoid(string eventName, string? oldStatus, string newStatus) =>
        Assert.False(_requested.Match(Context(eventName, oldStatus, newStatus)));

    [Theory]
    [MemberData(nameof(NeverRequested))]
    public void Declined_ShouldNotMatch_RegistrationOrVoid(string eventName, string? oldStatus, string newStatus) =>
        Assert.False(_declined.Match(Context(eventName, oldStatus, newStatus)));
}
