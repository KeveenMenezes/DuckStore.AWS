#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

namespace Ordering.Application.Orders.Queries.GetOrdersByName;

public class GetOrdersByNameHandler(
    IOrderRepository orderRepository)
    : IQueryHandler<GetOrdersByNameQuery, GetOrdersByNameResult>
{
    public async Task<GetOrdersByNameResult> Handle(
        GetOrdersByNameQuery query, CancellationToken cancellationToken)
    {
        var orders = orderRepository.GetOrdersByNameAsync(query.Name);

        return new GetOrdersByNameResult(orders.ToOrderDtoList(cancellationToken));
    }
}
