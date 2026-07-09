namespace Ordering.Function.Modules.Orders.Domain.Dtos;

public record PaymentDto(
    string CardName,
    string CardNumber,
    string Expiration,
    string Cvv,
    PaymentMethod PaymentMethod,
    int Installments);
