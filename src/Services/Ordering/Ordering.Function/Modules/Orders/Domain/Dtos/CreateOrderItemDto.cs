namespace Ordering.Function.Modules.Orders.Domain.Dtos;

public record CreateOrderItemDto(
    Guid ProductId,
    int Quantity,
    decimal Price);
