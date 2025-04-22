using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

namespace Ordering.UnitTests.Application.Queries;

public class GetOrdersByNameTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly GetOrdersByNameHandler _handler;

    public GetOrdersByNameTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _handler = new GetOrdersByNameHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOrders_WhenValidNameProvided()
    {
        // Arrange
        var name = "John";
        var query = new GetOrdersByNameQuery(name);

        _orderRepository
            .Setup(repo => repo.GetOrdersByNameAsync(name))
            .Returns(OrderDataTests.GetOrdersStreamMockAsync(10));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Orders);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmpty_WhenNoOrdersForName()
    {
        // Arrange
        var name = "NonExistentName";
        var query = new GetOrdersByNameQuery(name);

        _orderRepository
            .Setup(repo => repo.GetOrdersByNameAsync(name))
            .Returns(OrderDataTests.GetOrdersStreamMockAsync(0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Orders);
    }
}
