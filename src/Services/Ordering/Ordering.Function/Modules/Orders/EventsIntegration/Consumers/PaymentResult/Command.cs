namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.PaymentResult;

public record ApplyPaymentResultCommand(Guid OrderId, bool Authorized) : ICommand;
