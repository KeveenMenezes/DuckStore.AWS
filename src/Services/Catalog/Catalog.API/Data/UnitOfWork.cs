using BuildingBlocks.Core.Abstractions;

namespace Catalog.API.Data;

public class UnitOfWork(
    IDocumentSession session
) : IUnitOfWork
{
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await session.SaveChangesAsync(cancellationToken);
    }
}
