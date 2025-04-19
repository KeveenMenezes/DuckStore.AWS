using Ordering.Domain.ValueObjects;

namespace Ordering.UnitTests.Application.Queries;

public class GetOrdersHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly GetOrdersHandler _handler;

    public GetOrdersHandlerTests()
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
            .Returns(GetOrdersStreamMockAsync());

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
            .Returns(GetOrdersStreamMockAsync(0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Orders.Data);
    }

    private static async IAsyncEnumerable<Order> GetOrdersStreamMockAsync(int count = 10)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return OrderWithItems(i);
            await Task.Yield();
        }
    }

    private static Order OrderWithItems(int version)
    {
        var address = Address.Of(
            $"{version}firstName",
            $"{version}lastName",
            $"{version}@gmail.com",
            $"Bahcelievler No: {version}",
            "Turkey",
            "Istanbul",
            "38050");

        var payment = Payment.Of(
            $"{version}card",
            "5555555555554444",
            "12/28",
            "123",
            Domain.Enums.PaymentMethod.Credit);

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of($"ORD_{version}"),
            shippingAddress: address,
            billingAddress: address,
            payment);

        order.Add(
            ProductId.Of(Guid.NewGuid()),
            2 * version,
            20 * version);

        order.Add(
            ProductId.Of(Guid.NewGuid()),
            1 * version,
            10 * version);

        return order;
    }
}
