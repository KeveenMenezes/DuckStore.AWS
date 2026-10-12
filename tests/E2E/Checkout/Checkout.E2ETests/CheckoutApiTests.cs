namespace Checkout.E2ETests;

// The checkout saga's contract as a client sees it (ADR-0050), driven through the deployed BFF and
// AppSync with a real customer session.
public sealed class CheckoutApiTests(StorefrontSession session) : IClassFixture<StorefrontSession>
{
    private readonly Storefront _storefront = new(session);

    private async Task<List<Storefront.CartLine>> FreshCartAsync()
    {
        var products = await _storefront.ProductsAsync(2);
        var lines = new List<Storefront.CartLine> { new(products[0], 2), new(products[1], 1) };
        await _storefront.StoreCartAsync(lines);
        return lines;
    }

    [E2EFact]
    public async Task Checkout_ReturnsAnOrder_AndRepeatingTheAttemptReturnsTheSameOrder()
    {
        var lines = await FreshCartAsync();
        var checkoutId = Guid.NewGuid().ToString();
        var input = Storefront.CheckoutInput(await _storefront.QuoteAsync(lines), checkoutId);

        var first = Storefront.OrderIdOf(await _storefront.CheckoutAsync(input));
        // A retry after a lost response: same attempt, same order, no second checkout (ADR-0050 §3).
        var retry = Storefront.OrderIdOf(await _storefront.CheckoutAsync(input));

        Assert.Equal(first, retry);
    }

    [E2EFact]
    public async Task ConcurrentSubmitsOfOneAttempt_ResolveToOneOrder()
    {
        // A double click: two requests of the same attempt racing through the saga (ADR-0050 §3/§4).
        var lines = await FreshCartAsync();
        var input = Storefront.CheckoutInput(await _storefront.QuoteAsync(lines), Guid.NewGuid().ToString());

        var responses = await Task.WhenAll(_storefront.CheckoutAsync(input), _storefront.CheckoutAsync(input));

        Assert.Equal(Storefront.OrderIdOf(responses[0]), Storefront.OrderIdOf(responses[1]));
    }

    [E2EFact]
    public async Task OrderLines_ComeFromCatalogAndPricing_NotFromTheClientsCart()
    {
        // The cart was stored with a 0.01 price and, for the first line, a forged name (ADR-0050 §5).
        var products = await _storefront.ProductsAsync(2);
        var lines = new List<Storefront.CartLine> { new(products[0], 2), new(products[1], 1) };
        await _storefront.StoreCartAsync(lines, forgedNameForFirstLine: "FORGED NAME");
        var orderId = Storefront.OrderIdOf(await _storefront.CheckoutAsync(
            Storefront.CheckoutInput(await _storefront.QuoteAsync(lines), Guid.NewGuid().ToString())));

        var orderLines = await _storefront.OrderLinesAsync(orderId, TimeSpan.FromSeconds(30));

        Assert.Equal(2, orderLines.Count);
        foreach (var line in lines)
        {
            var ordered = Assert.Single(orderLines, o => o.ProductId == line.Product.Id);
            Assert.Equal(line.Product.Name, ordered.ProductName);
            // The main image comes from Catalog too — "My orders" renders it from this key.
            Assert.Equal(line.Product.MainImageId, ordered.ImageId);
            Assert.Equal(line.Quantity, ordered.Quantity);
            Assert.True(ordered.Price > 0.01m, $"{line.Product.Name} was ordered at the client's price {ordered.Price}");
        }
    }

    [E2EFact]
    public async Task Checkout_OfACartAlreadyClosed_IsRefusedAsBadRequest()
    {
        var lines = await FreshCartAsync();
        var total = await _storefront.QuoteAsync(lines);
        Storefront.OrderIdOf(await _storefront.CheckoutAsync(Storefront.CheckoutInput(total, Guid.NewGuid().ToString())));

        var second = await _storefront.CheckoutAsync(Storefront.CheckoutInput(total, Guid.NewGuid().ToString()));

        Assert.Equal("BadRequest", second.ErrorType);
    }

    [E2EFact]
    public async Task Checkout_WithATotalTheCustomerWasNotShown_IsRefusedWithTheServerTotal()
    {
        var lines = await FreshCartAsync();
        var serverTotal = await _storefront.QuoteAsync(lines);

        var response = await _storefront.CheckoutAsync(Storefront.CheckoutInput(serverTotal - 1m, Guid.NewGuid().ToString()));

        Assert.Equal("PriceChanged", response.ErrorType);
        Assert.Equal(serverTotal, (decimal)response.ErrorInfo!["serverTotal"]!);
    }

    [E2EFact]
    public async Task Checkout_WithAnInvalidCard_IsRefusedNamingTheCardField()
    {
        var lines = await FreshCartAsync();

        var response = await _storefront.CheckoutAsync(
            Storefront.CheckoutInput(await _storefront.QuoteAsync(lines), Guid.NewGuid().ToString(), cardNumber: Storefront.InvalidLuhnCard));

        Assert.Equal("InvalidCard", response.ErrorType);
        Assert.Equal("CardNumber", (string?)response.ErrorInfo!["field"]);
    }

    [E2EFact]
    public async Task Checkout_WithAMalformedCheckoutId_IsRefusedAsBadRequest()
    {
        var lines = await FreshCartAsync();

        var response = await _storefront.CheckoutAsync(
            Storefront.CheckoutInput(await _storefront.QuoteAsync(lines), checkoutId: "not-a-uuid"));

        Assert.Equal("BadRequest", response.ErrorType);
    }

    [E2EFact]
    public async Task Checkout_WithCash_NeedsNoCard()
    {
        var lines = await FreshCartAsync();
        var total = await _storefront.QuoteAsync(lines, Storefront.Payment.Cash);

        var response = await _storefront.CheckoutAsync(
            Storefront.CheckoutInput(total, Guid.NewGuid().ToString(), Storefront.Payment.Cash));

        Storefront.OrderIdOf(response);
    }
}
