using BuildingBlocks.Core.Abstractions;

namespace Ordering.Infrastructure.RepositoryAdapters;

public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}
