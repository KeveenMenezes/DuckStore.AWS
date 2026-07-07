using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using CatalogView.Function.Modules.Products.Queries.SearchProducts;

namespace CatalogView.UnitTests.Products;

public class SearchProductsHandlerTests
{
    [Fact]
    public async Task HandleAsync_MapsRequestToCriteria_AndResultToResponse()
    {
        var index = new Mock<IProductSearchIndex>();
        ProductSearchCriteria? captured = null;

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
            .Setup(i => i.SearchAsync(It.IsAny<ProductSearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback<ProductSearchCriteria, CancellationToken>((criteria, _) => captured = criteria)
            .ReturnsAsync(new ProductSearchResult([document], "next-token"));

        var handler = new SearchProductsHandler(index.Object);

        var request = new SearchProductsRequest
        {
            Query = "duck",
            SortBy = "AverageRating",
            Descending = true,
            MinRating = 3,
            MaxRating = 5,
            PageSize = 10,
            NextToken = "prev-token"
        };

        var response = await handler.HandleAsync(request);

        Assert.NotNull(captured);
        Assert.Equal("duck", captured!.Query);
        Assert.Equal(ProductSortField.AverageRating, captured.SortBy);
        Assert.True(captured.Descending);
        Assert.Equal(3, captured.MinRating);
        Assert.Equal(5, captured.MaxRating);
        Assert.Equal(10, captured.PageSize);
        Assert.Equal("prev-token", captured.NextToken);

        Assert.Single(response.Items);
        Assert.Equal("product-1", response.Items[0].Id);
        Assert.Equal(4.5, response.Items[0].AverageRating);
        Assert.Equal(2, response.Items[0].RatingCount);
        Assert.Equal("next-token", response.NextToken);
    }

    [Fact]
    public async Task HandleAsync_DefaultsPageSize_WhenNotPositive()
    {
        var index = new Mock<IProductSearchIndex>();
        ProductSearchCriteria? captured = null;

        index
            .Setup(i => i.SearchAsync(It.IsAny<ProductSearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback<ProductSearchCriteria, CancellationToken>((criteria, _) => captured = criteria)
            .ReturnsAsync(new ProductSearchResult([], null));

        var handler = new SearchProductsHandler(index.Object);

        await handler.HandleAsync(new SearchProductsRequest { PageSize = 0 });

        Assert.Equal(20, captured!.PageSize);
        Assert.Equal(ProductSortField.Relevance, captured.SortBy);
    }
}
