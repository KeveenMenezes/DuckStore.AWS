namespace Ordering.UnitTests.DataTests;

public static class BasketCheckoutEventDataTests
{
    public static BasketCheckoutEvent CreateValidBasketCheckoutEvent()
    {
        return new BasketCheckoutEvent
        {
            OwnerId = "USER#testuser",
            CustomerId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            TotalPrice = 1000m,
            ShippingAddress = new BasketCheckoutAddress
            {
                FirstName = "John",
                LastName = "Doe",
                EmailAddress = "john.doe@example.com",
                AddressLine = "123 Test Street",
                Country = "Testland",
                State = "Teststate",
                ZipCode = "12345",
            },
            Payment = new BasketCheckoutPayment
            {
                PaymentMethod = 1,
            },
            Items =
            [
                new BasketCheckoutItem(Guid.NewGuid(), "Rubber Duck Classic", null, 2, 500m),
                new BasketCheckoutItem(Guid.NewGuid(), "Rubber Duck Pirate", null, 1, 400m)
            ]
        };
    }
}
