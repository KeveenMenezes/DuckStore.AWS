namespace Ordering.Application.Orders.Queries.GetOrders;

public class GetOrdersHandler(
    IOrderRepository orderRepository)
    : IQueryHandler<GetOrdersQuery, GetOrdersResult>
{
    public async Task<GetOrdersResult> Handle(
        GetOrdersQuery query, CancellationToken cancellationToken)
    {
        var pageIndex = query.PaginationRequest.PageIndex;
        var pageSize = query.PaginationRequest.PageSize;

        var totalCount = await orderRepository.GetTotalCountOrders(cancellationToken);

        var orders = orderRepository.GetOrdersPaginationAsync(pageIndex, pageSize);

        return new GetOrdersResult(
            new PaginatedResult<OrderDto>(
                pageIndex,
                pageSize,
                totalCount,
                orders.ToOrderDtoList(cancellationToken)));
    }
}
