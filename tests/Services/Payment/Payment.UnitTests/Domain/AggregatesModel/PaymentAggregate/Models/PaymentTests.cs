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
    public void ApplyPaymentResult_ShouldTransitionToAuthorized_WhenPendingAndAuthorized()
    {
        var payment = PaymentDataTests.CreatePendingPayment();

        payment.ApplyPaymentResult(authorized: true, "SIM-123");

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("SIM-123", payment.AuthorizationCode);
    }

    [Fact]
    public void ApplyPaymentResult_ShouldTransitionToDeclined_WhenPendingAndNotAuthorized()
    {
        var payment = PaymentDataTests.CreatePendingPayment();

        payment.ApplyPaymentResult(authorized: false, "card_declined");

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal("card_declined", payment.DeclineReason);
    }

    [Fact]
    public void ApplyPaymentResult_ShouldBeNoOp_WhenAlreadyAuthorizedAndAuthorizedAgain()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        payment.ApplyPaymentResult(authorized: true, "SIM-first");

        payment.ApplyPaymentResult(authorized: true, "SIM-second");

        Assert.Equal("SIM-first", payment.AuthorizationCode);
    }

    [Fact]
    public void ApplyPaymentResult_ShouldBeNoOp_WhenAlreadyAuthorizedAndDeclined()
    {
        var payment = PaymentDataTests.CreatePendingPayment();
        payment.ApplyPaymentResult(authorized: true, "SIM-123");

        payment.ApplyPaymentResult(authorized: false, "card_declined");

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Null(payment.DeclineReason);
    }
}
