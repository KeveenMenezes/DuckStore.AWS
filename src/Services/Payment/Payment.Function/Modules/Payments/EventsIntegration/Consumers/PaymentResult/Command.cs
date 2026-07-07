namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.PaymentResult;

public record ApplyPaymentResultCommand(
    Guid PaymentId,
    Guid OrderId,
    bool Authorized,
    string Detail) : ICommand;
