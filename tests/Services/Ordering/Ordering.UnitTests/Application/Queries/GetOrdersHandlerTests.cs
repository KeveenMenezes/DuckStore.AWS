//using Ordering.Domain.Abstractions.Repositories;
//
//public class GetOrdersHandlerTests
//{
//    [Fact]
//    public async Task Handle_ShouldReturnPaginatedResult_WhenCalled()
//    {
//        // Arrange
//        var mockOrderRepository = new Mock<IOrderRepository>();
//        var query = new GetOrdersQuery
//        {
//            PaginationRequest = new PaginationRequest { PageIndex = 1, PageSize = 10 }
//        };
//
//        mockOrderRepository
//            .Setup(repo => repo.GetTotalCountOrders(It.IsAny<CancellationToken>()))
//            .ReturnsAsync(100);
//
//        mockOrderRepository
//            .Setup(repo => repo.GetOrdersPaginationAsync(It.IsAny<int>(), It.IsAny<int>()))
//            .ReturnsAsync(new List<Order> { new Order() });
//
//        var handler = new GetOrdersHandler(mockOrderRepository.Object);
//
//        // Act
//        var result = await handler.Handle(query, CancellationToken.None);
//
//        // Assert
//        Assert.NotNull(result);
//        Assert.Equal(1, result.PaginatedResult.PageIndex);
//        Assert.Equal(10, result.PaginatedResult.PageSize);
//        Assert.Equal(100, result.PaginatedResult.TotalCount);
//        Assert.NotEmpty(result.PaginatedResult.Items);
//    }
//}
//