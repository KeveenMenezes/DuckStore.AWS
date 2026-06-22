namespace Catalog.UnitTests.Products;

public class GetProductByIdQueryHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly GetProductByIdQueryHandler _handler;

    public GetProductByIdQueryHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _productRepositoryMock = _autoMocker.GetMock<IProductRepository>();
        _handler = new GetProductByIdQueryHandler(_productRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnProduct_WhenProductExists()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = Product.Create(
            productId, "Product1", "Description1", "http://image1.url", 50.0m, 1, [CategoryId.Of(Guid.NewGuid())]);

        _productRepositoryMock
            .Setup(repo => repo.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var query = new GetProductByIdQuery(productId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(productId, result.Product.Id.Value);
        Assert.Equal("Product1", result.Product.Name);
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = Guid.NewGuid();

        _productRepositoryMock
            .Setup(repo => repo.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var query = new GetProductByIdQuery(productId);

        // Act & Assert
        await Assert.ThrowsAsync<ProductNotFoundException>(() =>
            _handler.Handle(query, CancellationToken.None));
    }
}
