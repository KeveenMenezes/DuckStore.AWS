namespace Payment.UnitTests.DataTests;

public static class OrderCreatedEventDataTests
{
    public static OrderCreatedEvent CreateValidOrderCreatedEvent() => new()
    {
        OrderId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        OrderName = "john.doe@example.com",
        Status = 2,

        FirstName = "John",
        LastName = "Doe",
        EmailAddress = "john.doe@example.com",
        AddressLine = "123 Test Street",
        Country = "Testland",
        State = "Teststate",
        ZipCode = "12345",

        CardName = "John Doe",
        CardNumber = "4111111111111111",
        Expiration = "12/25",
        Cvv = "123",
        PaymentMethod = 1,

        Items =
        [
            new OrderCreatedItem(Guid.NewGuid(), 2, 500),
            new OrderCreatedItem(Guid.NewGuid(), 1, 400)
        ]
    };
}
