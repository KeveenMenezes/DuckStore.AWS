namespace Ordering.Function.Dtos;

public record CreateOrderItemDto(
    Guid ProductId,
    int Quantity,
    decimal Price);
