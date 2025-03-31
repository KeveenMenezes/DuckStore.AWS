namespace Ordering.Domain.Abstractions.Repositories;

public interface IOrderRepository : IRepository<Order>
{
    IAsyncEnumerable<Order> GetOrdersByNameAsync(
        string name, CancellationToken cancellationToken = default);

}
