using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync.Strategies;

namespace CatalogView.UnitTests.Products;

public class ProductSyncStrategyTests
{
    [Fact]
    public void CanHandle_OnlyProductSyncedEvent()
    {
        var strategy = new ProductSyncStrategy(Mock.Of<IProductSearchIndex>());

        Assert.True(strategy.CanHandle(nameof(ProductSyncedEvent)));
        Assert.False(strategy.CanHandle(nameof(ProductDeletedEvent)));
    }

    [Fact]
    public async Task HandleAsync_UpsertsDocument()
    {
        var index = new Mock<IProductSearchIndex>();
        var strategy = new ProductSyncStrategy(index.Object);

        var evt = new ProductSyncedEvent
        {
            ProductId = Guid.NewGuid().ToString(),
            Name = "Debug Duck",
            Description = "A duck",
            Images = [new ProductImageData("01HZXW5N8T2J3K4M5P6Q7R8S9A", true, 0)],
            Stock = 10,
            CategoryIds = ["cat-1"],
            CategoryNames = ["Languages"]
        };

        await strategy.HandleAsync("event-1", JsonSerializer.SerializeToElement(evt));

        index.Verify(
            i => i.UpsertAsync(
                It.Is<SearchDocument>(d =>
                    d.Id == evt.ProductId &&
                    d.Name == evt.Name &&
                    d.Description == evt.Description &&
                    d.Images.Count == 1 &&
                    d.Images[0].ImageId == evt.Images[0].ImageId &&
                    d.Images[0].IsMain &&
                    d.Stock == evt.Stock &&
                    d.CategoryIds.SequenceEqual(evt.CategoryIds) &&
                    d.Categories.Count == 1 &&
                    d.Categories[0].Id == "cat-1" &&
                    d.Categories[0].Name == "Languages"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        index.Verify(i => i.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
