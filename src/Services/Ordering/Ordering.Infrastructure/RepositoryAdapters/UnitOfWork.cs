using BuildingBlocks.Core.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ordering.Infrastructure.RepositoryAdapters;

public class UnitOfWork(
    ApplicationDbContext context)
    : IUnitOfWork
{
    private IDbContextTransaction? _currentTransaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
            return;
        _currentTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
            return;

        await context.SaveChangesAsync(cancellationToken);
        await _currentTransaction.CommitAsync(cancellationToken);
        await _currentTransaction.DisposeAsync();

        _currentTransaction = null;
    }
}
