namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.PaymentResult;

public class ApplyPaymentResultHandler(IPaymentRepository paymentRepository)
    : ICommandHandler<ApplyPaymentResultCommand>
{
    public async Task<Unit> Handle(ApplyPaymentResultCommand command, CancellationToken cancellationToken)
    {
        var payment = await ApplyResultAsync(paymentRepository, command, cancellationToken);

        if (payment is not null)
            await paymentRepository.AddAsync(payment, cancellationToken);

        return Unit.Value;
    }

    // Rehydrates the Payment and applies the authorize/decline transition in-memory, without
    // persisting — the caller decides how to persist (a plain AddAsync here, or, for the
    // EventBridge consumer, a TransactWriteItem inside the idempotency inbox transaction).
    internal static async Task<Domain.Entities.Payment?> ApplyResultAsync(
        IPaymentRepository paymentRepository,
        ApplyPaymentResultCommand command,
        CancellationToken cancellationToken = default)
    {
        var payment = await paymentRepository.GetByIdAsync(command.PaymentId, cancellationToken);
        if (payment is null)
            return null;

        payment.ApplyPaymentResult(command.Authorized, command.Detail);

        return payment;
    }
}
