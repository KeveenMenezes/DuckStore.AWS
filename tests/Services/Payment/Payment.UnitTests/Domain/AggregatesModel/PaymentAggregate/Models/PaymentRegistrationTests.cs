using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Domain.AggregatesModel.PaymentAggregate.Models;

// The payment is registered at checkout and only promoted once the cart closed (ADR-0050 §6).
public class PaymentRegistrationTests
{
    private static readonly CardDetails Card = PaymentDataTests.CreateCardDetails();

    private static PaymentEntity Registered(decimal amount = 150m)
    {
        var orderId = Guid.NewGuid();
        return PaymentEntity.Register(PaymentId.Of(orderId), orderId, Guid.NewGuid(), amount, Card);
    }

    [Fact]
    public void Register_ShouldStartRegistered()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var payment = PaymentEntity.Register(PaymentId.Of(orderId), orderId, customerId, 150m, Card);

        Assert.Equal(PaymentStatus.Registered, payment.Status);
        Assert.Equal(orderId, payment.Id.Value);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal(customerId, payment.CustomerId);
        Assert.Equal(150m, payment.Amount);
        Assert.Equal(Card, payment.Card);
        Assert.NotNull(payment.CreatedAt);
    }

    [Fact]
    public void Register_ShouldThrow_WithoutACustomer() =>
        Assert.ThrowsAny<ArgumentException>(() =>
            PaymentEntity.Register(PaymentId.Of(Guid.NewGuid()), Guid.NewGuid(), Guid.Empty, 150m, Card));

    [Fact]
    public void Register_ShouldThrow_WithoutAnOrder() =>
        Assert.ThrowsAny<ArgumentException>(() =>
            PaymentEntity.Register(PaymentId.Of(Guid.NewGuid()), Guid.Empty, Guid.NewGuid(), 150m, Card));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Register_ShouldThrow_WithoutAPositiveAmount(decimal amount) =>
        Assert.ThrowsAny<ArgumentException>(() => Registered(amount));

    [Fact]
    public void Void_ShouldVoidARegisteredPayment()
    {
        var payment = Registered();

        Assert.True(payment.Void());
        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }

    [Fact]
    public void Void_ShouldBeANoOp_WhenAlreadyVoided()
    {
        var payment = Registered();
        payment.Void();

        Assert.False(payment.Void());
        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }

    // The cart closed and the checkout event promoted the payment: a late compensation must not undo it.
    [Theory]
    [InlineData(true, PaymentStatus.AwaitingPoints)]
    [InlineData(false, PaymentStatus.Pending)]
    public void Void_ShouldBeANoOp_OncePromoted(bool awaitingPoints, PaymentStatus promoted)
    {
        var payment = Registered();
        payment.Promote(awaitingPoints);

        Assert.False(payment.Void());
        Assert.Equal(promoted, payment.Status);
    }

    [Fact]
    public void Void_ShouldBeANoOp_ForALegacyPendingPayment()
    {
        var payment = PaymentDataTests.CreatePendingPayment();

        Assert.False(payment.Void());
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Theory]
    [InlineData(true, PaymentStatus.AwaitingPoints)]
    [InlineData(false, PaymentStatus.Pending)]
    public void Promote_ShouldMoveARegisteredPaymentOn(bool awaitingPoints, PaymentStatus expected)
    {
        var payment = Registered();

        Assert.True(payment.Promote(awaitingPoints));
        Assert.Equal(expected, payment.Status);
    }

    // Promotion is driven by BasketCheckoutEvent, which proves the cart closed under this OrderId:
    // a void that raced a successful close of the same attempt must not leave the order unpaid.
    [Fact]
    public void Promote_ShouldMoveAVoidedPaymentOn_BecauseTheCartClosed()
    {
        var payment = Registered();
        payment.Void();

        Assert.True(payment.Promote(awaitingPoints: false));
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    // A redelivered checkout event must not move a payment that already went on.
    [Fact]
    public void Promote_ShouldBeANoOp_OncePromoted()
    {
        var payment = Registered();
        payment.Promote(awaitingPoints: true);

        Assert.False(payment.Promote(awaitingPoints: false));
        Assert.Equal(PaymentStatus.AwaitingPoints, payment.Status);
    }

    [Fact]
    public void IsSameRegistration_ShouldBeTrue_ForTheSameTerms() =>
        Assert.True(Registered(150m).IsSameRegistration(150.00m, PaymentDataTests.CreateCardDetails()));

    [Fact]
    public void IsSameRegistration_ShouldBeFalse_ForADifferentAmount() =>
        Assert.False(Registered(150m).IsSameRegistration(151m, Card));

    [Fact]
    public void IsSameRegistration_ShouldBeFalse_ForADifferentMethod() =>
        Assert.False(Registered().IsSameRegistration(
            150m, CardDetails.Of(Card.CardNumber, Card.Expiration, Card.Cvv, PaymentMethod.Debit)));

    [Fact]
    public void IsSameRegistration_ShouldBeFalse_ForADifferentCard() =>
        Assert.False(Registered().IsSameRegistration(
            150m, CardDetails.Of("5555555555554444", Card.Expiration, Card.Cvv, Card.PaymentMethod)));

    [Fact]
    public void IsSameRegistration_ShouldBeFalse_OnceVoided()
    {
        var payment = Registered();
        payment.Void();

        Assert.False(payment.IsSameRegistration(150m, Card));
    }

    [Fact]
    public void IsSameRegistration_ShouldBeFalse_OncePromoted()
    {
        var payment = Registered();
        payment.Promote(awaitingPoints: false);

        Assert.False(payment.IsSameRegistration(150m, Card));
    }
}
