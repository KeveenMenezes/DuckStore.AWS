using BuildingBlocks.Core.Abstractions;

namespace Basket.API.Data;

using Marten;

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
