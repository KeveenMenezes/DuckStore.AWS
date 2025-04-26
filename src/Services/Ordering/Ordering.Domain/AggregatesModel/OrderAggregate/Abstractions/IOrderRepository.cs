namespace Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

public interface IOrderRepository : IRepository<Order>
{
    IAsyncEnumerable<Order> GetOrdersPaginationStream(int pageIndex, int pageSize);

    IAsyncEnumerable<Order> GetOrdersByNameAsync(string name);

    IAsyncEnumerable<Order> GetOrdersByCustomerAsync(Guid customerId);

    Task<long> GetTotalCountOrders(CancellationToken cancellationToken = default);
}
