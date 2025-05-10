namespace Catalog.API.Data;

public class CatalogInitialData : IInitialData
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
                new Guid("6cd2ae29-decf-4898-800d-5ecb9884f46c"),
                "Description 1",
                "product-1.png",
                "https://s.yimg.com/ny/api/res/1.2/1KPwRUrDJIrTid9e6.UwqA--/YXBwaWQ9aGlnaGxhbmRlcjt3PTEyMDA7aD02MzI-/https://s.yimg.com/os/creatr-uploaded-images/2023-06/d377ed90-1059-11ee-be4e-0a70c44f0039",
                100,
                ["A", "B"]),
            new(
                Guid.NewGuid(),
                "Description 2",
                "product-2.png",
                "https://s.yimg.com/ny/api/res/1.2/1KPwRUrDJIrTid9e6.UwqA--/YXBwaWQ9aGlnaGxhbmRlcjt3PTEyMDA7aD02MzI-/https://s.yimg.com/os/creatr-uploaded-images/2023-06/d377ed90-1059-11ee-be4e-0a70c44f0039",
                50,
                ["A", "B"]),
        ];
}
