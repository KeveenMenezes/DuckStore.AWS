using Ordering.Application.Dtos;
using Ordering.Application.Orders.Commands.CreateOrder;
using Ordering.Domain.Enums;

namespace Ordering.UnitTests.Application.Commands.CreateOrder;

public class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_ShouldCreateOrderSuccessfully()
    {
        // Arrange
        var mockOrderRepository = new Mock<IOrderRepository>();
        var handler = new CreateOrderHandler(mockOrderRepository.Object);

        var orderDto = new OrderDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Test Order",
            new AddressDto("John", "Doe", "john.doe@example.com", "123 Street", "Country", "State", "12345"),
            new AddressDto("John", "Doe", "john.doe@example.com", "123 Street", "Country", "State", "12345"),
            new PaymentDto("CardName", "1234567890123456", "12/25", "123", PaymentMethod.Debit),
            OrderStatus.Pending,
            [
                new OrderItemDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    2,
                    50)
            ]);

        var command = new CreateOrderCommand(orderDto);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        mockOrderRepository.Verify(repo => repo.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
