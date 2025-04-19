namespace Ordering.UnitTests.DataTests;

public static class OrderingDataTests
{
    public static async IAsyncEnumerable<Order> GetOrdersStreamMockAsync(int count = 10)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return CreateOrderWithItems(i);
            await Task.Yield();
        }
    }

    private static Order CreateOrderWithItems(int version)
    {
        var address = Address.Of(
            $"{version}firstName",
            $"{version}lastName",
            $"{version}@gmail.com",
            $"Bahcelievler No: {version}",
            "Turkey",
            "Istanbul",
            "38050");

        var payment = Payment.Of(
            $"{version}card",
            "5555555555554444",
            "12/28",
            "123",
            Domain.Enums.PaymentMethod.Credit);

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of($"ORD_{version}"),
            shippingAddress: address,
            billingAddress: address,
            payment);

        order.Add(
            ProductId.Of(Guid.NewGuid()),
            2 * version,
            20 * version);

        order.Add(
            ProductId.Of(Guid.NewGuid()),
            1 * version,
            10 * version);

        return order;
    }
}
