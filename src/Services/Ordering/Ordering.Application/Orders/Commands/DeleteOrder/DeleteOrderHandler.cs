namespace Ordering.Application.Orders.Commands.DeleteOrder;

public class DeleteOrderHandler(
    IOrderRepository orderRepository)
    : ICommandHandler<DeleteOrderCommand, DeleteOrderResult>
{
    public async Task<DeleteOrderResult> Handle(
        DeleteOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(command.OrderId, cancellationToken) ??
            throw new OrderNotFoundBadRequestException(command.OrderId);

        orderRepository.Delete(order, cancellationToken);

        return new DeleteOrderResult(true);
    }
}
