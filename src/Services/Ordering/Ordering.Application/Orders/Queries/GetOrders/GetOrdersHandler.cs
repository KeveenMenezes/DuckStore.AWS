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

        var totalCountTask = orderRepository.GetTotalCountOrders(cancellationToken);
        var ordersStream = orderRepository.GetOrdersPaginationStream(pageIndex, pageSize);

        return new GetOrdersResult(
            new PaginatedResult<OrderDto>(
                pageIndex,
                pageSize,
                await totalCountTask,
                ordersStream.ToOrderDtoList(cancellationToken)));
    }
}
