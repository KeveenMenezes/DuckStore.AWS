using BuildingBlocks.Core.Abstractions;

namespace Basket.API.Data;

public class UnitOfWork(
    IDocumentSession session
) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await session.SaveChangesAsync(cancellationToken);
    }
}
