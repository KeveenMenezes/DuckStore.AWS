namespace Ordering.Application.Dtos;

public record PaymentDto(
    string CardName,
    string CardNumber,
    string Expiration,
    string Cvv,
    PaymentMethod PaymentMethod);

public enum PaymentMethod
{
    Debit = 1,
    Credit = 2,
}