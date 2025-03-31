namespace Ordering.Application.Orders.Queries.GetOrdersByName;

public class GetOrdersByNameHandler(
    IOrderRepository orderRepository)
    : IQueryHandler<GetOrdersByNameQuery, GetOrdersByNameResult>
{
    public async Task<GetOrdersByNameResult> Handle(
        GetOrdersByNameQuery query, CancellationToken cancellationToken)
    {
        var orders = orderRepository.GetOrdersByNameAsync(
            query.Name, cancellationToken);

        return new GetOrdersByNameResult(orders.ToOrderDtoList());
    }
}
