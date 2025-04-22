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
        var billingAddress = CreateBillingAddressWithVersion(suffix);
        var payment = CreatePaymentWithVersion(suffix);

        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderName.Of($"ORD_{suffix}"),
            shippingAddress,
            billingAddress,
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

    public static Address CreateBillingAddressWithVersion(string? suffix = null) =>
        Address.Of(
            $"{suffix}FirstNameBilling",
            $"{suffix}LastNameBillinng",
            $"{suffix}Billing@gmail.com",
            $"BahcelievlerBilling No: {suffix}",
            "TurkeyBilling",
            "IstanbulBilling",
            "38051");

    public static Payment CreatePaymentWithVersion(string? suffix = null) =>
        Payment.Of(
            $"{suffix}card",
            "5555555555554444",
            "12/28",
            "123",
            PaymentMethod.Credit);
}
