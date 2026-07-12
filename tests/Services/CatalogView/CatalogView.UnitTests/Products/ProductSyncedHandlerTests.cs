using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ProductSync;

namespace CatalogView.UnitTests.Products;

public class CatalogProductSyncHandlerTests
{
    [Theory]
    [InlineData("INSERT")]
    [InlineData("MODIFY")]
    public async Task HandleAsync_UpsertsDocument_OnInsertOrModify(string changeType)
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new CatalogProductSyncHandler(index.Object);

        var evt = new CatalogProductSyncEvent
        {
            ChangeType = changeType,
            ProductId = Guid.NewGuid().ToString(),
            Name = "Debug Duck",
            Description = "A duck",
            ImageUrl = "/duck.jpg",
            Stock = 10,
            CategoryIds = ["cat-1"],
            CategoryNames = ["Languages"]
        };

        await handler.HandleAsync(evt);

        index.Verify(
            i => i.UpsertAsync(
                It.Is<SearchDocument>(d =>
                    d.Id == evt.ProductId &&
                    d.Name == evt.Name &&
                    d.Description == evt.Description &&
                    d.ImageUrl == evt.ImageUrl &&
                    d.Stock == evt.Stock &&
                    d.CategoryIds.SequenceEqual(evt.CategoryIds) &&
                    d.Categories.Count == 1 &&
                    d.Categories[0].Id == "cat-1" &&
                    d.Categories[0].Name == "Languages"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        index.Verify(i => i.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DeletesDocument_OnRemove()
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new CatalogProductSyncHandler(index.Object);

        var evt = new CatalogProductSyncEvent { ChangeType = "REMOVE", ProductId = "product-1" };

        await handler.HandleAsync(evt);

        index.Verify(i => i.DeleteAsync("product-1", It.IsAny<CancellationToken>()), Times.Once);
        index.Verify(i => i.UpsertAsync(It.IsAny<SearchDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
