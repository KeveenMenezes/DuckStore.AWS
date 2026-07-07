namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.PaymentResult;

public static class PaymentResultMapper
{
    public static ApplyPaymentResultCommand ToApplyPaymentResultCommand(PaymentAuthorizedEvent message) =>
        new(message.OrderId, Authorized: true);

    public static ApplyPaymentResultCommand ToApplyPaymentResultCommand(PaymentDeclinedEvent message) =>
        new(message.OrderId, Authorized: false);
}
