using Ordering.Application.Extensions;

namespace Ordering.Application.Orders.Queries.GetOrdersByName;

public class GetOrdersByNameHandler(
    IOrderRepository orderRepository)
    : IQueryHandler<GetOrdersByNameQuery, GetOrdersByNameResult>
{
    public async Task<GetOrdersByNameResult> Handle(
        GetOrdersByNameQuery request, CancellationToken cancellationToken)
    {
        var orders = orderRepository.GetOrdersByNameAsync(
            request.Name, cancellationToken);

        return new GetOrdersByNameResult(orders.ToOrderDtoList());
    }
}
