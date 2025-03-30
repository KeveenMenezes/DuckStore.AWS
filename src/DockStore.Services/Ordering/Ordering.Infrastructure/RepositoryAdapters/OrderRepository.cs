using Ordering.Domain.Abstractions.Repositories;

namespace Ordering.Infrastructure.RepositoryAdapters;

public class OrderRepository(DbContext context)
    : Repository<Order>(context), IOrderRepository
{
}
