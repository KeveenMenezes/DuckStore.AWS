using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class CategorySyncStrategyTests
{
    [Fact]
    public void CanHandle_OnlyCatalogCategorySyncEvent()
    {
        var strategy = new CategorySyncStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(CatalogCategorySyncEvent)));
        Assert.False(strategy.CanHandle(nameof(ProductSyncedEvent)));
    }

    [Fact]
    public async Task HandleAsync_RenamesCategoryOnEveryReferencingProduct()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new CategorySyncStrategy(index.Object);

        var evt = new CatalogCategorySyncEvent { CategoryId = "cat-1", Name = "Programming Languages" };

        await strategy.HandleAsync("event-1", JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.RenameCategoryAsync("cat-1", "Programming Languages", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
