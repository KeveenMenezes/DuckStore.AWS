using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class ProductDeleteStrategyTests
{
    [Fact]
    public void CanHandle_OnlyProductDeletedEvent()
    {
        var strategy = new ProductDeleteStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(ProductDeletedEvent)));
        Assert.False(strategy.CanHandle(nameof(ProductSyncedEvent)));
    }

    [Fact]
    public async Task HandleAsync_DeletesDocument()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ProductDeleteStrategy(index.Object);

        var evt = new ProductDeletedEvent { ProductId = "product-1" };

        await strategy.HandleAsync("event-1", JsonSerializer.SerializeToElement(evt));

        index.Verify(i => i.DeleteAsync("product-1", It.IsAny<CancellationToken>()), Times.Once);
        index.Verify(i => i.UpsertAsync(It.IsAny<SearchDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
