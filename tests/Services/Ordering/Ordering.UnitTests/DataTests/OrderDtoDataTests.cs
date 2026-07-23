namespace Ordering.UnitTests.DataTests;

public static class CreateOrderCommandTestsDataTests
{
    public static CreateOrderCommand CreateOrderDtoWithValidItems(Guid? orderId = null) =>
        new(
            orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "Test Order",
            new AddressDto(
                "John",
                "Doe",
                "john.doe@example.com",
                "123 Street",
                "Country",
                "State",
                "12345"),
            new PaymentDto(PaymentMethod.Debit, 1),
            [
                new CreateOrderItemDto(
                    Guid.NewGuid(),
                    "Rubber Duck Classic",
                    null,
                    2,
                    50)
            ]
        );

    public static CreateOrderCommand CreateOrderDtoWithInvalidItems() =>
        new(
            Guid.Empty,
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
            new PaymentDto(0, 1),
            []);
}
