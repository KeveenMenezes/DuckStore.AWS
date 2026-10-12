namespace Payment.UnitTests.Domain.AggregatesModel.PaymentAggregate.ValueObjects;

// The checkout validates the card before registering the payment (ADR-0050 §6): Luhn, a non-expired
// MM/YYYY and a 3–4 digit CVV for card methods; cash needs none.
public class CardValidationTests
{
    private const string ValidNumber = "4111111111111111";

    private static CardDetails Validate(
        string number = ValidNumber, string expiration = "12/2030", string cvv = "123",
        PaymentMethod method = PaymentMethod.Credit) =>
        CardDetails.Validated(number, expiration, cvv, method, FixedClock.October2026);

    private static string FieldOf(Action act) => Assert.Throws<InvalidCardException>(act).Field;

    [Theory]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Debit)]
    public void Validated_ShouldAcceptAValidCard(PaymentMethod method)
    {
        var card = Validate(method: method);

        Assert.Equal(ValidNumber, card.CardNumber);
        Assert.Equal("12/2030", card.Expiration);
        Assert.Equal(method, card.PaymentMethod);
    }

    [Fact]
    public void Validated_ShouldAcceptAFourDigitCvv() =>
        Assert.Equal("1234", Validate(cvv: "1234").Cvv);

    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("4111-1111-1111-111a")]
    [InlineData("")]
    public void Validated_ShouldRefuseTheNumber_WhenItFailsLuhn(string number) =>
        Assert.Equal("CardNumber", FieldOf(() => Validate(number: number)));

    [Fact]
    public void Validated_ShouldAcceptSeparators_InAValidNumber() =>
        Validate(number: "4111 1111-1111 1111");

    // Cards are valid through the last day of their expiration month.
    [Fact]
    public void Validated_ShouldAcceptACardExpiringThisMonth() =>
        Validate(expiration: "10/2026");

    [Theory]
    [InlineData("09/2026")]
    [InlineData("12/2025")]
    public void Validated_ShouldRefuseAnExpiredCard(string expiration) =>
        Assert.Equal("Expiration", FieldOf(() => Validate(expiration: expiration)));

    [Theory]
    [InlineData("12/30")]
    [InlineData("13/2030")]
    [InlineData("00/2030")]
    [InlineData("1/2030")]
    [InlineData("12-2030")]
    [InlineData(" 12/2030")]
    [InlineData("")]
    public void Validated_ShouldRefuseAMalformedExpiration(string expiration) =>
        Assert.Equal("Expiration", FieldOf(() => Validate(expiration: expiration)));

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData("")]
    public void Validated_ShouldRefuseACvvThatIsNotThreeOrFourDigits(string cvv) =>
        Assert.Equal("Cvv", FieldOf(() => Validate(cvv: cvv)));

    [Fact]
    public void Validated_ShouldNotRequireACard_ForCash()
    {
        var card = Validate(number: "", expiration: "", cvv: "", method: PaymentMethod.Cash);

        Assert.Equal(PaymentMethod.Cash, card.PaymentMethod);
    }

    [Fact]
    public void Validated_ShouldThrow_ForAnUnknownMethod() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(method: (PaymentMethod)9));

    // The refusal message reaches the client; it must never echo the card back.
    [Fact]
    public void InvalidCard_ShouldNotCarryTheCardInItsMessage()
    {
        var ex = Assert.Throws<InvalidCardException>(() => Validate(number: "4111111111111112"));

        Assert.DoesNotContain("4111111111111112", ex.Message);
    }
}
