using BuildingBlocks.Core.Abstractions;

namespace Catalog.API.Data;

public class UnitOfWork(
    IDocumentSession session
) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await session.SaveChangesAsync(cancellationToken);
    }
}
