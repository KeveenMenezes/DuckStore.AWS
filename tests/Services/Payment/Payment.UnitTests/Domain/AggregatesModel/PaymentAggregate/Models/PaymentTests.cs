namespace Payment.UnitTests.Domain.AggregatesModel.PaymentAggregate.Models;

public class PaymentTests
{
    [Fact]
    public void Create_ShouldStartAsPending()
    {
        var payment = PaymentDataTests.CreatePendingPayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.AuthorizationCode);
        Assert.Null(payment.DeclineReason);
    }

    [Fact]
    public void Authorize_ShouldTransitionToAuthorized_WhenPending()
    {
        var payment = PaymentDataTests.CreatePendingPayment();

        payment.Authorize("SIM-123");

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("SIM-123", payment.AuthorizationCode);
    }

    [Fact]
    public void Decline_ShouldTransitionToDeclined_WhenPending()
    {
        var payment = PaymentDataTests.CreatePendingPayment();

        payment.Decline("card_declined");

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal("card_declined", payment.DeclineReason);
    }

    [Fact]
    public void Authorize_ShouldBeNoOp_WhenAlreadyAuthorized()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        payment.Authorize("SIM-first");

        payment.Authorize("SIM-second");

        Assert.Equal("SIM-first", payment.AuthorizationCode);
    }

    [Fact]
    public void Decline_ShouldBeNoOp_WhenAlreadyAuthorized()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        payment.Authorize("SIM-123");

        payment.Decline("card_declined");

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Null(payment.DeclineReason);
    }
}
