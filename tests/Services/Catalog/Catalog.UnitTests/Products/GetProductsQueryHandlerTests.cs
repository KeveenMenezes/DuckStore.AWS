namespace Catalog.UnitTests.Products;

public class GetProductsQueryHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly GetProductsQueryHandler _handler;

    public GetProductsQueryHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _productRepositoryMock = _autoMocker.GetMock<IProductRepository>();
        _handler = new GetProductsQueryHandler(_productRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnPagedProducts_WhenProductsExist()
    {
        // Arrange
        var products = new List<Product>
        {
            Product.Create(Guid.NewGuid(), "Product1", "Description1", "http://image1.url", 50.0m, 1, [CategoryId.Of(Guid.NewGuid())]),
            Product.Create(Guid.NewGuid(), "Product2", "Description2", "http://image2.url", 100.0m, 1, [CategoryId.Of(Guid.NewGuid())])
        };

        _productRepositoryMock
            .Setup(repo => repo.GetPagedAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResult<Product>(1, 2, products.Count, products));

        var query = new GetProductsQuery(1, 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.PaginatedProducts.Items.Count());
        Assert.Contains(result.PaginatedProducts.Items, p => p.Name == "Product1");
        Assert.Contains(result.PaginatedProducts.Items, p => p.Name == "Product2");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmpty_WhenNoProductsExist()
    {
        // Arrange
        _productRepositoryMock
            .Setup(repo => repo.GetPagedAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResult<Product>(1, 2, 0, []));

        var query = new GetProductsQuery(1, 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.PaginatedProducts.Items);
    }
}
