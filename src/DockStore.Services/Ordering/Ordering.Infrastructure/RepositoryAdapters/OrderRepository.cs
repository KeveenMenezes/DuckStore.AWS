namespace Ordering.Infrastructure.RepositoryAdapters;

public class OrderRepository(ApplicationDbContext db)
    : Repository<Order>(db), IOrderRepository
{
}