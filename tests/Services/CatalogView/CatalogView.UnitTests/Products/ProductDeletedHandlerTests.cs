using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ProductDeleted;

namespace CatalogView.UnitTests.Products;

public class ProductDeletedHandlerTests
{
    [Fact]
    public async Task HandleAsync_DeletesDocument()
    {
        var index = new Mock<IProductSearchIndex>();
        var handler = new ProductDeletedHandler(index.Object);

        var evt = new ProductDeletedEvent { ProductId = "product-1" };

        await handler.HandleAsync(evt);

        index.Verify(i => i.DeleteAsync("product-1", It.IsAny<CancellationToken>()), Times.Once);
        index.Verify(i => i.UpsertAsync(It.IsAny<SearchDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
