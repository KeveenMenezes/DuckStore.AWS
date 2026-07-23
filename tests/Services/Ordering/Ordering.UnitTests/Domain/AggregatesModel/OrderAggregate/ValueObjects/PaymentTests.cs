namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class PaymentTests
{
    [Fact]
    public void Of_ShouldCreatePaymentWithValidData()
    {
        // Arrange
        var paymentMethod = PaymentMethod.Credit;

        // Act
        var payment = Payment.Of(paymentMethod, 1);

        // Assert
        Assert.NotNull(payment);
        Assert.Equal(paymentMethod, payment.PaymentMethod);
        Assert.Equal(1, payment.Installments);
    }

    [Fact]
    public void Of_ShouldCreatePayment_ForCash()
    {
        // Arrange & Act
        var payment = Payment.Of(PaymentMethod.Cash, 1);

        // Assert
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
        Assert.Equal(1, payment.Installments);
    }
}
