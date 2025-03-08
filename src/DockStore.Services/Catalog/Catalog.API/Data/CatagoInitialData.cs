using Marten.Schema;

namespace Catalog.API.Data;

public class CatagoInitialData : IInitialData
{
    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        using var session = store.LightweightSession();

        if (await session.Query<Product>().AnyAsync(token: cancellation))
        {
            return;
        }

        session.Store(GetPreconfiguredProducts());

        await session.SaveChangesAsync(cancellation);
    }

    private static IEnumerable<Product> GetPreconfiguredProducts() =>
        [
            new(
                Guid.NewGuid(),
                "Description 1",
                "product-1.png",
                "http://externalcatalogbaseurltobereplaced/api/pic/1",
                100,
                ["A", "B"]),
            new(
                Guid.NewGuid(),
                "Description 2",
                "product-2.png",
                "http://externalcatalogbaseurltobereplaced/api/pic/2",
                50,
                ["A", "B"]),
        ];
}
