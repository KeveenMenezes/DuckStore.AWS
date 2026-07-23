namespace Payment.UnitTests.DataTests;

public static class PaymentDataTests
{
    public static Payment.Function.Modules.Payments.Domain.Entities.Payment CreatePendingPayment(
        Guid? orderId = null, Guid? customerId = null, decimal amount = 100m) =>
        Payment.Function.Modules.Payments.Domain.Entities.Payment.Create(
            PaymentId.Of(Guid.NewGuid()),
            orderId ?? Guid.NewGuid(),
            customerId ?? Guid.NewGuid(),
            amount,
            CreateCardDetails());

    public static CardDetails CreateCardDetails() =>
        CardDetails.Of("4111111111111111", "12/28", "123", PaymentMethod.Credit);
}
