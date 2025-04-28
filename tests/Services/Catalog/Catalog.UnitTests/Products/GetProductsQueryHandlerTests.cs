namespace Catalog.UnitTests.Products;

public class GetProductsQueryHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IDocumentSession> _sessionMock;
    private readonly GetProductsQueryHandler _handler;

    public GetProductsQueryHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _sessionMock = _autoMocker.GetMock<IDocumentSession>();
        _handler = new GetProductsQueryHandler(_sessionMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnPagedProducts_WhenProductsExist()
    {
        // Arrange
        var products = new List<Product>
        {
            new(
                Guid.NewGuid(),
                "Product1",
                "Description1",
                "http://image1.url",
                50.0m,
                ["Category1"]),

            new(Guid.NewGuid(),
                "Product2",
                "Description2",
                "http://image2.url",
                100.0m,
                ["Category2"])
        };

        _sessionMock.Setup(session =>
            session.Query<Product>())
                .Returns(
                    MartenMockExtensions
                        .CreateMartenQueryableMock(products));

        var query = new GetProductsQuery(1, 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Products.Count());
        Assert.Contains(result.Products, p =>
            p.Name == "Product1");
        Assert.Contains(result.Products, p =>
            p.Name == "Product2");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmpty_WhenNoProductsExist()
    {
        // Arrange
        var products = new List<Product>();

        _sessionMock.Setup(session =>
            session.Query<Product>())
                .Returns(
                    MartenMockExtensions
                        .CreateMartenQueryableMock(products));

        var query = new GetProductsQuery(1, 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Products);
    }
}
