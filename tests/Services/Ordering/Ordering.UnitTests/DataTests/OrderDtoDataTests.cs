namespace Ordering.UnitTests.DataTests;

public static class CreateOrderCommandTestsDataTests
{
    public static CreateOrderCommand CreateOrderDtoWithValidItems(Guid? orderId = null) =>
        new(
            orderId ?? Guid.NewGuid(),
            "Test Order",
            new AddressDto(
                "John",
                "Doe",
                "john.doe@example.com",
                "123 Street",
                "Country",
                "State",
                "12345"),
            new PaymentDto(
                "CardName",
                "4111111111111111",
                "12/25",
                "123",
                PaymentMethod.Debit),
            [
                new CreateOrderItemDto(
                    Guid.NewGuid(),
                    2,
                    50)
            ]
        );

    public static CreateOrderCommand CreateOrderDtoWithInvalidItems() =>
        new(
            Guid.Empty,
            string.Empty,
            new AddressDto(
                string.Empty,
                string.Empty,
                "invalid-email",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty),
            new PaymentDto(
                string.Empty,
                "invalid-card-number",
                "invalid-expiration",
                "invalid-cvv",
                0),
            []);
}
