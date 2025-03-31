namespace Ordering.Infrastructure.RepositoryAdapters;

public class OrderRepository(ApplicationDbContext dbContext)
    : Repository<Order>(dbContext), IOrderRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public IAsyncEnumerable<Order> GetOrdersByNameAsync(
        string name, CancellationToken cancellationToken = default) =>
        _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .Where(o => o.OrderName.Value.Contains(name))
            .AsAsyncEnumerable();
}