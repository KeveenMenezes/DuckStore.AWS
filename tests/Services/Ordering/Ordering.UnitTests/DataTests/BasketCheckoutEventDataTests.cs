namespace Ordering.UnitTests.DataTests;

public static class BasketCheckoutEventDataTests
{
    public static BasketCheckoutEvent CreateValidBasketCheckoutEvent()
    {
        return new BasketCheckoutEvent
        {
            UserName = "testuser",
            CustomerId = Guid.NewGuid(),
            TotalPrice = 1000m,
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
            PaymentMethod = 1
        };
    }
}
