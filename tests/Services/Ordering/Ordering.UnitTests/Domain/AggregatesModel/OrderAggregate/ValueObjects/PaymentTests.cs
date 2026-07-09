namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class PaymentTests
{
    [Fact]
    public void Of_ShouldCreatePaymentWithValidData()
    {
        // Arrange
        var cardName = "John Doe";
        var cardNumber = "1234567890123456";
        var expiration = "12/25";
        var cvv = "123";
        var paymentMethod = PaymentMethod.Credit;

        // Act
        var payment = Payment.Of(cardName, cardNumber, expiration, cvv, paymentMethod, 1);

        // Assert
        Assert.NotNull(payment);
        Assert.Equal(cardName, payment.CardName);
        Assert.Equal(cardNumber, payment.CardNumber);
        Assert.Equal(expiration, payment.Expiration);
        Assert.Equal(cvv, payment.Cvv);
        Assert.Equal(paymentMethod, payment.PaymentMethod);
        Assert.Equal(1, payment.Installments);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenCardNameIsNullOrWhiteSpace()
    {
        // Arrange
        var cardName = " ";
        var cardNumber = "1234567890123456";
        var expiration = "12/25";
        var cvv = "123";
        var paymentMethod = PaymentMethod.Credit;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Payment.Of(cardName, cardNumber, expiration, cvv, paymentMethod, 1));
    }

    [Fact]
    public void Of_ShouldThrowException_WhenCardNumberIsNullOrWhiteSpace()
    {
        // Arrange
        var cardName = "John Doe";
        var cardNumber = " ";
        var expiration = "12/25";
        var cvv = "123";
        var paymentMethod = PaymentMethod.Credit;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Payment.Of(cardName, cardNumber, expiration, cvv, paymentMethod, 1));
    }

    [Fact]
    public void Of_ShouldThrowException_WhenCvvIsNullOrWhiteSpace()
    {
        // Arrange
        var cardName = "John Doe";
        var cardNumber = "1234567890123456";
        var expiration = "12/25";
        var cvv = " ";
        var paymentMethod = PaymentMethod.Credit;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Payment.Of(cardName, cardNumber, expiration, cvv, paymentMethod, 1));
    }

    [Fact]
    public void Of_ShouldThrowException_WhenCvvLengthIsGreaterThanThree()
    {
        // Arrange
        var cardName = "John Doe";
        var cardNumber = "1234567890123456";
        var expiration = "12/25";
        var cvv = "1234";
        var paymentMethod = PaymentMethod.Credit;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.Of(cardName, cardNumber, expiration, cvv, paymentMethod, 1));
    }

    [Fact]
    public void Of_ShouldCreatePayment_ForCash_WithEmptyCardFields()
    {
        // Arrange & Act
        var payment = Payment.Of("", "", "", "", PaymentMethod.Cash, 1);

        // Assert
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
        Assert.Equal("", payment.CardNumber);
    }
}
