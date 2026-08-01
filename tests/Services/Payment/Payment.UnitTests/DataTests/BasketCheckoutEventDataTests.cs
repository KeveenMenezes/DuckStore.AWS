namespace Payment.UnitTests.DataTests;

public static class BasketCheckoutEventDataTests
{
    public static BasketCheckoutEvent CreateValidBasketCheckoutEvent() => new()
    {
        OwnerId = "USER#testuser",
        CustomerId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        TotalPrice = 1400m,
        DiscountId = null,

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
            CardName = "John Doe",
            CardNumber = "4111111111111111",
            Expiration = "12/25",
            Cvv = "123",
            PaymentMethod = 1,
            Installments = 1,
        },
    };
}
