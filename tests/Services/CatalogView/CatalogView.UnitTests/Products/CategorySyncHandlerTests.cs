using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CategorySync;

namespace CatalogView.UnitTests.Products;

public class CategorySyncHandlerTests
{
    [Fact]
    public async Task HandleAsync_RenamesCategoryOnEveryReferencingProduct()
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new CategorySyncHandler(index.Object);

        var evt = new CatalogCategorySyncEvent { CategoryId = "cat-1", Name = "Programming Languages" };

        await handler.HandleAsync(evt);

        index.Verify(
            i => i.RenameCategoryAsync("cat-1", "Programming Languages", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
