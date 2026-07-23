namespace Ordering.Function.Modules.Orders.Domain.Dtos;

public record PaymentDto(PaymentMethod PaymentMethod, int Installments);
