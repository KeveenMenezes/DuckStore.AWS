namespace Ordering.UnitTests.Application.Queries;

public class GetOrdersByCustomerHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly GetOrdersByCustomerHandler _handler;

    public GetOrdersByCustomerHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _orderRepository = _autoMocker.GetMock<IOrderRepository>();
        _handler = new GetOrdersByCustomerHandler(_orderRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOrders_WhenValidCustomerIdProvided()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var query = new GetOrdersByCustomerQuery(customerId);

        _orderRepository
            .Setup(repo => repo.GetOrdersByCustomerAsync(customerId))
            .Returns(OrderingDataTests.GetOrdersStreamMockAsync(10));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Orders);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmpty_WhenNoOrdersForCustomer()
    {
        // Arrange
        var query = new GetOrdersByCustomerQuery(It.IsAny<Guid>());

        _orderRepository
            .Setup(repo => repo.GetOrdersByCustomerAsync(It.IsAny<Guid>()))
            .Returns(OrderingDataTests.GetOrdersStreamMockAsync(0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Orders);
    }
}
