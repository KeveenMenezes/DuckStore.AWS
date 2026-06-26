namespace Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task DeleteAsync(Order order, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Order> GetOrdersPaginationStream(int pageIndex, int pageSize);

    IAsyncEnumerable<Order> GetOrdersByNameAsync(string name);

    IAsyncEnumerable<Order> GetOrdersByCustomerAsync(Guid customerId);

    IAsyncEnumerable<Order> GetOrdersByStatusAsync(OrderStatus status);

    Task<long> GetTotalCountOrders(CancellationToken cancellationToken = default);
}
