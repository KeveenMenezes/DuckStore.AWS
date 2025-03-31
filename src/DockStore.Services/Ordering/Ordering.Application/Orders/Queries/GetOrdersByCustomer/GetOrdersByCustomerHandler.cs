namespace Ordering.Application.Orders.Queries.GetOrdersByCustomer;

public class GetOrdersByCustomerHandler(IOrderRepository orderRepository)
    : IQueryHandler<GetOrdersByCustomerQuery, GetOrdersByCustomerResult>
{
    public async Task<GetOrdersByCustomerResult> Handle(
        GetOrdersByCustomerQuery request, CancellationToken cancellationToken)
    {
        var orders = orderRepository.GetOrdersByCustomerAsync(
            request.CustomerId);

        return new GetOrdersByCustomerResult(orders.ToOrderDtoList(cancellationToken));
    }
}
