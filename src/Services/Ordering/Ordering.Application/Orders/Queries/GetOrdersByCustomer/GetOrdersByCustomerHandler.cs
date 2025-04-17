#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

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
