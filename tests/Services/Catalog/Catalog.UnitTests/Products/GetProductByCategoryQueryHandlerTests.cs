namespace Catalog.UnitTests.Products;

public class GetProductByCategoryQueryHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IDocumentSession> _sessionMock;
    private readonly GetProductByCategoryQueryHandler _handler;

    public GetProductByCategoryQueryHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _sessionMock = _autoMocker.GetMock<IDocumentSession>();
        _handler = new GetProductByCategoryQueryHandler(_sessionMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnProducts_WhenCategoryExists()
    {
        // Arrange
        var category = "Category1";

        var products = new List<Product>
        {
            new(
                Guid.NewGuid(),
                "Product1",
                "Description1",
                "http://image1.url",
                50.0m,
                [category]),

            new(
                Guid.NewGuid(),
                "Product2",
                "Description2",
                "http://image2.url",
                100.0m,
                [category])
        };

        _sessionMock.Setup(session =>
            session.Query<Product>())
                .Returns(
                    MartenMockExtensions
                        .CreateMartenQueryableMock(products).Object);

        var query = new GetProductByCategoryQuery(category);

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
        var category = "NonExistentCategory";
        var products = new List<Product>();

        var query = new GetProductByCategoryQuery(category);

        _sessionMock.Setup(session =>
            session.Query<Product>())
                .Returns(MartenMockExtensions.CreateMartenQueryableMock(products).Object);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Products);
    }
}
