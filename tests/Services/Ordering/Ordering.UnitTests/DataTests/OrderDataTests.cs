namespace Ordering.UnitTests.DataTests;

public static class OrderDataTests
{
    public static async IAsyncEnumerable<Order> GetOrdersStreamMockAsync(int count = 10)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return CreateOrderWithItems(i.ToString());
            await Task.Yield();
        }
    }

    public static Order CreateOrderWithItems(string? suffix = null)
    {
        var shippingAddress = CreateShippingAddressWithVersion(suffix);
        var payment = CreatePaymentWithVersion(suffix);

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of($"ORD_{suffix}"),
            shippingAddress,
            payment);

        order.Add(
            ProductId.Of(Guid.NewGuid()),
            1,
            10);

        order.Add(
            ProductId.Of(Guid.NewGuid()),
            2,
            20);

        return order;
    }

    public static Address CreateShippingAddressWithVersion(string? suffix = null) =>
        Address.Of(
            $"{suffix}FirstNameShipping",
            $"{suffix}LastNameShipping",
            $"{suffix}Shipping@gmail.com",
            $"BahcelievlerShipping No: {suffix}",
            "TurkeyShipping",
            "IstanbulShipping",
            "38050");

    public static Payment CreatePaymentWithVersion(string? suffix = null) =>
        Payment.Of(
            $"{suffix}card",
            "5555555555554444",
            "12/28",
            "123",
            PaymentMethod.Credit);
}
