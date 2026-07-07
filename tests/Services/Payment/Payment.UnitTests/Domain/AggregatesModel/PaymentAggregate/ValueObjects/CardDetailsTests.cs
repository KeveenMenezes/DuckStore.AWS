namespace Payment.UnitTests.Domain.AggregatesModel.PaymentAggregate.ValueObjects;

public class CardDetailsTests
{
    [Fact]
    public void Of_ShouldCreateCardDetails_WhenValid()
    {
        var card = CardDetails.Of("4111111111111111", "12/28", "123", PaymentMethod.Credit);

        Assert.Equal("4111111111111111", card.CardNumber);
        Assert.Equal(PaymentMethod.Credit, card.PaymentMethod);
    }

    [Fact]
    public void Of_ShouldThrow_WhenCardNumberIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => CardDetails.Of("", "12/28", "123", PaymentMethod.Credit));
    }

    [Fact]
    public void Of_ShouldThrow_WhenCvvIsTooLong()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CardDetails.Of("4111111111111111", "12/28", "1234", PaymentMethod.Credit));
    }

    [Fact]
    public void Equality_ShouldBeByValue()
    {
        var card1 = CardDetails.Of("4111111111111111", "12/28", "123", PaymentMethod.Credit);
        var card2 = CardDetails.Of("4111111111111111", "12/28", "123", PaymentMethod.Credit);

        Assert.Equal(card1, card2);
    }
}
