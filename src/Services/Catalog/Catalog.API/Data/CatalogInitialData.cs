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
        session.Store(GetPreconfiguredCategories());

        await session.SaveChangesAsync(cancellation);
    }

    private static IEnumerable<Product> GetPreconfiguredProducts()
    {
        var categoryIds = new List<CategoryId>
        {
            CategoryId.Of(Guid.NewGuid()),
            CategoryId.Of(Guid.NewGuid())
        };

        return
        [
            Product.Create(
                new Guid("6cd2ae29-decf-4898-800d-5ecb9884f46c"),
                "Product 1",
                "Description 1",
                "https://s.yimg.com/ny/api/res/1.2/1KPwRUrDJIrTid9e6.UwqA--/YXBwaWQ9aGlnaGxhbmRlcjt3PTEyMDA7aD02MzI-/https://s.yimg.com/os/creatr-uploaded-images/2023-06/d377ed90-1059-11ee-be4e-0a70c44f0039",
                100,
                10,
                categoryIds
            ),
            Product.Create(
                Guid.NewGuid(),
                "Product 2",
                "Description 2",
                "https://s.yimg.com/ny/api/res/1.2/1KPwRUrDJIrTid9e6.UwqA--/YXBwaWQ9aGlnaGxhbmRlcjt3PTEyMDA7aD02MzI-/https://s.yimg.com/os/creatr-uploaded-images/2023-06/d377ed90-1059-11ee-be4e-0a70c44f0039",
                50,
                5,
                categoryIds
            )
        ];
    }

    private static IEnumerable<Category> GetPreconfiguredCategories()
    {
        var categoryId1 = CategoryId.Of(Guid.NewGuid());
        return
        [
            Category.Create(
                categoryId1,
                "Category 1",
                null
            ),
            Category.Create(
                CategoryId.Of(Guid.NewGuid()),
                "Category 2",
                categoryId1
            )
        ];
    }
}
