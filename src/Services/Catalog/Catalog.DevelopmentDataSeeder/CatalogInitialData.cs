using Catalog.Function.Modules.Categories.Data;
using Catalog.Function.Modules.Categories.Domain.Entities;
using Catalog.Function.Modules.Categories.Domain.ValueObjects;
using Catalog.Function.Modules.Products.Data;
using Catalog.Function.Modules.Products.Domain.Entities;
using Catalog.Function.Modules.Products.Domain.ValueObjects;

namespace Catalog.DevelopmentDataSeeder;

public class CatalogInitialData(IProductRepository productRepository, ICategoryRepository categoryRepository)
{
    // Stable category GUIDs so product→category FK is consistent across DynamoDB Local restarts.
    private static readonly Guid ClassicsId = new("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid LanguagesId = new("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid FrameworksId = new("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid SpecialsId = new("a1000000-0000-0000-0000-000000000004");

    // Seeded products carry no images (ADR-0034, supersedes ADR-0018): Images holds metadata
    // referencing uploads in the product-images pipeline, and dev-seeded data has none —
    // frontends render a placeholder for an empty list.
    private static List<ProductImage> NoImages => [];

    public async Task PopulateAsync(CancellationToken cancellationToken = default)
    {
        if (await productRepository.AnyAsync(cancellationToken))
            return;

        foreach (var category in GetPreconfiguredCategories())
            await categoryRepository.AddAsync(category, cancellationToken);

        foreach (var product in GetPreconfiguredProducts())
            await productRepository.AddAsync(product, cancellationToken);
    }

    private static IEnumerable<Category> GetPreconfiguredCategories()
    {
        var classicsId = CategoryId.Of(ClassicsId);
        var languagesId = CategoryId.Of(LanguagesId);
        var frameworksId = CategoryId.Of(FrameworksId);
        var specialsId = CategoryId.Of(SpecialsId);

        return
        [
            Category.Create(classicsId,   "Classics"),
            Category.Create(languagesId,  "Languages"),
            Category.Create(frameworksId, "Frameworks"),
            Category.Create(specialsId,   "Specials"),
        ];
    }

    private static IEnumerable<Product> GetPreconfiguredProducts()
    {
        var classics = new List<CategoryId> { CategoryId.Of(ClassicsId) };
        var languages = new List<CategoryId> { CategoryId.Of(LanguagesId) };
        var frameworks = new List<CategoryId> { CategoryId.Of(FrameworksId) };
        var specials = new List<CategoryId> { CategoryId.Of(SpecialsId) };

        return
        [
            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000001"),
                "Debug Duck Classic",
                "The classic rubber duck for debugging. Your most loyal coding companion.",
                NoImages, 50, classics),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000002"),
                "Python Duck",
                "Duck with a Python snake skin. Ideal for devs who love indentation.",
                NoImages, 30, languages),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000003"),
                "JavaScript Duck",
                "Vibrant yellow duck with the JS logo. For those who live in console.log().",
                NoImages, 45, languages),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000004"),
                "Full Stack Duck",
                "Premium duck with layers representing frontend, backend and database.",
                NoImages, 15, specials),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000005"),
                "DevOps Duck",
                "Duck with a construction helmet and Docker logo. Deploy without fear!",
                NoImages, 25, specials),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000006"),
                "TypeScript Duck",
                "A typed and safe duck. A guarantee of zero any in your code.",
                NoImages, 35, languages),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000007"),
                "React Duck",
                "Duck with a spinning propeller on its hat. An infinite re-render of cuteness!",
                NoImages, 40, frameworks),

            Product.Create(
                new Guid("b1000000-0000-0000-0000-000000000008"),
                "Ratuna Duck",
                "Duck with a relational database on its chest. SELECT * FROM ducks.",
                NoImages, 20, languages),
        ];
    }
}
