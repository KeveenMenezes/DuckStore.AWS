using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Catalog.Function.Modules.Categories.Data;
using Catalog.Function.Modules.Categories.Domain.Entities;
using Catalog.Function.Modules.Categories.Domain.ValueObjects;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers;
using Catalog.Function.Modules.Products.EventsIntegration.Publishers.Rules;

namespace Catalog.UnitTests.EventsIntegration;

public class ProductSyncedRuleTests
{
    private static CatalogStreamImage NewProduct(string id, List<string>? categoryIds = null) =>
        new(id, "Debug Duck", "A duck", [], 10, categoryIds ?? []);

    [Theory]
    [InlineData("INSERT")]
    [InlineData("MODIFY")]
    public void Match_ShouldBeTrue_OnInsertOrModify(string eventName)
    {
        var categoryRepository = new Mock<ICategoryRepository>();
        var rule = new ProductSyncedRule(categoryRepository.Object);
        var product = NewProduct("id-1");
        var context = new StreamContext<CatalogStreamImage>(eventName, product, product);

        Assert.True(rule.Match(context));
    }

    [Fact]
    public void Match_ShouldBeFalse_OnRemove()
    {
        var categoryRepository = new Mock<ICategoryRepository>();
        var rule = new ProductSyncedRule(categoryRepository.Object);
        var context = new StreamContext<CatalogStreamImage>("REMOVE", NewProduct("id-1"), null);

        Assert.False(rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_ShouldHydrateCategoryNames()
    {
        var categoryId = Guid.NewGuid();
        var category = Category.Create(CategoryId.Of(categoryId), "Languages");

        var categoryRepository = new Mock<ICategoryRepository>();
        categoryRepository
            .Setup(r => r.GetByIdsAsync(It.Is<IEnumerable<Guid>>(ids => ids.Contains(categoryId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([category]);

        var rule = new ProductSyncedRule(categoryRepository.Object);
        var product = NewProduct("id-1", [categoryId.ToString()]);
        var context = new StreamContext<CatalogStreamImage>("INSERT", null, product);

        var instruction = await rule.BuildAsync(context);

        Assert.Equal(nameof(ProductSyncedEvent), instruction.DetailType);
        var payload = Assert.IsType<ProductSyncedEvent>(instruction.Payload);
        Assert.Equal("id-1", payload.ProductId);
        Assert.Equal(["Languages"], payload.CategoryNames);
    }
}
