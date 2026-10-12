using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Domain.AggregatesModel.PaymentAggregate.Models;

// A checkout that used points waits for Challenges to reserve them before the gateway is asked —
// otherwise a payment could be authorized with a discount the customer didn't have (ADR-0048 §6).
public class PaymentPointsTests
{
    private static PaymentEntity Create(bool awaitingPoints) =>
        PaymentEntity.Create(
            PaymentId.Of(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 190m,
            PaymentDataTests.CreateCardDetails(), awaitingPoints: awaitingPoints);

    [Fact]
    public void Create_ShouldAwaitPoints_WhenTheCheckoutUsedPoints() =>
        Assert.Equal(PaymentStatus.AwaitingPoints, Create(awaitingPoints: true).Status);

    [Fact]
    public void Create_ShouldStillStartPending_WithoutPoints() =>
        Assert.Equal(PaymentStatus.Pending, Create(awaitingPoints: false).Status);

    [Fact]
    public void ConfirmPoints_ShouldMoveToPending()
    {
        var payment = Create(awaitingPoints: true);

        payment.ConfirmPoints();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void RejectPoints_ShouldDecline_WithInsufficientPoints()
    {
        var payment = Create(awaitingPoints: true);

        payment.RejectPoints();

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal("InsufficientPoints", payment.DeclineReason);
    }

    // A redelivered reservation event must not move a payment the gateway already settled.
    [Fact]
    public void ConfirmPoints_ShouldBeANoOp_OnceThePaymentLeftAwaitingPoints()
    {
        var payment = Create(awaitingPoints: true);
        payment.ConfirmPoints();
        payment.ApplyPaymentResult(authorized: true, "AUTH-1");

        payment.ConfirmPoints();
        payment.RejectPoints();

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Null(payment.DeclineReason);
    }

    // The gateway result only ever applies to a Pending payment — never to one still waiting on
    // points, and never to one the points rejection declined (Payment's own consumer receives the
    // PaymentDeclinedEvent that rejection publishes).
    [Fact]
    public void ApplyPaymentResult_ShouldBeANoOp_WhileAwaitingPoints()
    {
        var payment = Create(awaitingPoints: true);

        payment.ApplyPaymentResult(authorized: true, "AUTH-1");

        Assert.Equal(PaymentStatus.AwaitingPoints, payment.Status);
    }

    [Fact]
    public void ApplyPaymentResult_ShouldBeANoOp_AfterThePointsWereRejected()
    {
        var payment = Create(awaitingPoints: true);
        payment.RejectPoints();

        payment.ApplyPaymentResult(authorized: false, "InsufficientPoints");

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal("InsufficientPoints", payment.DeclineReason);
    }
}
