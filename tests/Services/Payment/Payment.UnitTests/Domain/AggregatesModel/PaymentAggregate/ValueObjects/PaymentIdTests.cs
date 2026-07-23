namespace Payment.UnitTests.Domain.AggregatesModel.PaymentAggregate.ValueObjects;

public class PaymentIdTests
{
    [Fact]
    public void Of_ShouldCreatePaymentIdWithValidGuid()
    {
        var validGuid = Guid.NewGuid();

        var paymentId = PaymentId.Of(validGuid);

        Assert.NotNull(paymentId);
        Assert.Equal(validGuid, paymentId.Value);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenGuidIsEmpty()
    {
        Assert.Throws<PaymentIdBadRequestException>(() => PaymentId.Of(Guid.Empty));
    }
}
