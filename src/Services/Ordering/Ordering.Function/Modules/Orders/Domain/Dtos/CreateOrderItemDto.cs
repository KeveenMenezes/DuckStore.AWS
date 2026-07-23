namespace Ordering.Function.Modules.Orders.Domain.Dtos;

public record CreateOrderItemDto(
    Guid ProductId,
    string ProductName,
    string? ImageId,
    int Quantity,
    decimal Price);
