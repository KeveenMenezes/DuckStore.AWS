using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;
using Ordering.Domain.AggregatesModel.OrderAggregate.Models;
using Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

namespace Ordering.Infrastructure.RepositoryAdapters;

public class OrderRepository(ApplicationDbContext dbContext)
    : Repository<Order>(dbContext), IOrderRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public IAsyncEnumerable<Order> GetOrdersByNameAsync(string name) =>
        _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .Where(o => o.OrderName.Value.Contains(name))
            .AsAsyncEnumerable();

    public IAsyncEnumerable<Order> GetOrdersByCustomerAsync(Guid customerId) =>
        _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .Where(o => o.CustomerId == CustomerId.Of(customerId))
            .AsAsyncEnumerable();

    public IAsyncEnumerable<Order> GetOrdersPaginationStream(
        int pageIndex, int pageSize) =>
        _dbContext.Orders
            .OrderBy(o => o.OrderName.Value)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .AsAsyncEnumerable();

    public async Task<long> GetTotalCountOrders(CancellationToken cancellationToken = default) =>
        await _dbContext.Orders.LongCountAsync(cancellationToken);
}
