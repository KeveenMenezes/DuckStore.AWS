using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

namespace Ordering.UnitTests.Application.Queries;

public class GetOrdersTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly GetOrdersHandler _handler;

    public GetOrdersTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _handler = new GetOrdersHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnPaginatedResult_WhenValidQueryProvided()
    {
        // Arrange
        var query = new GetOrdersQuery(
            new PaginationRequest { PageIndex = 1, PageSize = 5 });

        _orderRepository
            .Setup(repo => repo.GetTotalCountOrders(It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        _orderRepository
            .Setup(repo => repo.GetOrdersPaginationStream(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(OrderDataTests.GetOrdersStreamMockAsync());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Orders.PageIndex);
        Assert.Equal(5, result.Orders.PageSize);
        Assert.Equal(10, result.Orders.Count);
        Assert.NotEmpty(result.Orders.Data);
    }

    [Fact]
    public async Task Handle_ShouldNotReturnPaginatedResult_WhenNotValidQueryProvided()
    {
        // Arrange
        var query = new GetOrdersQuery(
            new PaginationRequest { PageIndex = 1, PageSize = 5 });

        _orderRepository
            .Setup(repo => repo.GetTotalCountOrders(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _orderRepository
            .Setup(repo => repo.GetOrdersPaginationStream(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(OrderDataTests.GetOrdersStreamMockAsync(0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Orders.Data);
    }
}
