namespace Ordering.Function.Features.GetOrdersByCustomer;

public class GetOrdersByCustomerHandler(IOrderRepository orderRepository)
    : IQueryHandler<GetOrdersByCustomerQuery, GetOrdersByCustomerResult>
{
    public async Task<GetOrdersByCustomerResult> Handle(
        GetOrdersByCustomerQuery request, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.GetOrdersByCustomerAsync(request.CustomerId, cancellationToken);

        return new GetOrdersByCustomerResult([.. orders.Select(o => o.ToOrderDto())]);
    }
}
