namespace Ordering.Function.Features.DeleteOrder;

public class DeleteOrderHandler(
    IOrderRepository orderRepository)
    : ICommandHandler<DeleteOrderCommand, DeleteOrderResult>
{
    public async Task<DeleteOrderResult> Handle(
        DeleteOrderCommand command, CancellationToken cancellationToken)
    {
        _ = await orderRepository.GetByIdAsync(command.OrderId, cancellationToken) ??
            throw new OrderNotFoundBadRequestException(command.OrderId);

        await orderRepository.DeleteAsync(command.OrderId, cancellationToken);

        return new DeleteOrderResult(true);
    }
}
