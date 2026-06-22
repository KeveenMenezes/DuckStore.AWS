namespace Catalog.UnitTests.Products;

public class GetProductByCategoryQueryHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly GetProductByCategoryQueryHandler _handler;

    public GetProductByCategoryQueryHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _productRepositoryMock = _autoMocker.GetMock<IProductRepository>();
        _handler = new GetProductByCategoryQueryHandler(_productRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnProducts_WhenCategoryExists()
    {
        // Arrange
        var categoryId = Guid.NewGuid();

        var products = new List<Product>
        {
            Product.Create(Guid.NewGuid(), "Product1", "Description1", "http://image1.url", 50.0m, 1, [CategoryId.Of(categoryId)]),
            Product.Create(Guid.NewGuid(), "Product2", "Description2", "http://image2.url", 100.0m, 1, [CategoryId.Of(categoryId)])
        };

        _productRepositoryMock
            .Setup(repo => repo.GetByCategoryAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var query = new GetProductByCategoryQuery(categoryId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Products.Count());
        Assert.Contains(result.Products, p => p.Name == "Product1");
        Assert.Contains(result.Products, p => p.Name == "Product2");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmpty_WhenCategoryDoesNotExist()
    {
        // Arrange
        var categoryId = Guid.NewGuid();

        _productRepositoryMock
            .Setup(repo => repo.GetByCategoryAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var query = new GetProductByCategoryQuery(categoryId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Products);
    }
}
