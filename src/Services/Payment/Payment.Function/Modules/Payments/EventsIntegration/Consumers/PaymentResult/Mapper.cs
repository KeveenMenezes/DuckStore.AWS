namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.PaymentResult;

public static class PaymentResultMapper
{
    public static ApplyPaymentResultCommand ToApplyPaymentResultCommand(PaymentAuthorizedEvent message) =>
        new(message.PaymentId, message.OrderId, Authorized: true, Detail: message.AuthorizationCode);

    public static ApplyPaymentResultCommand ToApplyPaymentResultCommand(PaymentDeclinedEvent message) =>
        new(message.PaymentId, message.OrderId, Authorized: false, Detail: message.DeclineReason);
}
