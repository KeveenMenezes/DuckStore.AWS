using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.Queries.GetProduct;

namespace CatalogView.UnitTests.Products;

public class GetProductHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsNull_WhenDocumentMissing()
    {
        var index = new Mock<IProductSearchIndex>();
        index
            .Setup(i => i.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchDocument?)null);

        var handler = new GetProductHandler(index.Object);

        var response = await handler.HandleAsync(new GetProductRequest { Id = "missing" });

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_MapsDocumentToResponse_WhenFound()
    {
        var index = new Mock<IProductSearchIndex>();
        var document = new SearchDocument
        {
            Id = "product-1",
            Name = "Debug Duck",
            Description = "A duck",
            ImageUrl = "/duck.jpg",
            Price = 29.90m,
            Stock = 10,
            CategoryIds = ["cat-1"],
            AverageRating = 4.5,
            RatingCount = 2
        };

        index
            .Setup(i => i.GetAsync("product-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var handler = new GetProductHandler(index.Object);

        var response = await handler.HandleAsync(new GetProductRequest { Id = "product-1" });

        Assert.NotNull(response);
        Assert.Equal(document.Id, response!.Id);
        Assert.Equal(document.Name, response.Name);
        Assert.Equal(document.AverageRating, response.AverageRating);
        Assert.Equal(document.RatingCount, response.RatingCount);
    }
}
