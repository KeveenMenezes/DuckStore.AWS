namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.PaymentResult;

public class ApplyPaymentResultHandler(IOrderRepository orderRepository)
    : ICommandHandler<ApplyPaymentResultCommand>
{
    public async Task<Unit> Handle(ApplyPaymentResultCommand command, CancellationToken cancellationToken)
    {
        var order = await ApplyResultAsync(orderRepository, command, cancellationToken);

        if (order is not null)
            await orderRepository.AddAsync(order, cancellationToken);

        return Unit.Value;
    }

    // Rehydrates the Order and applies the completed/cancelled transition in-memory, without
    // persisting — the caller decides how to persist (a plain AddAsync here, or, for the
    // EventBridge consumer, a TransactWriteItem inside the idempotency inbox transaction).
    internal static async Task<Order?> ApplyResultAsync(
        IOrderRepository orderRepository,
        ApplyPaymentResultCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
            return null;

        if (command.Authorized)
            order.MarkCompleted();
        else
            order.MarkCancelled();

        return order;
    }
}
