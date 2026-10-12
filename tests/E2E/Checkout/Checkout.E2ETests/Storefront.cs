using System.Text.Json.Nodes;

namespace Checkout.E2ETests;

// The storefront operations a checkout needs, as the SPA calls them through its BFF.
public sealed class Storefront(StorefrontSession session)
{
    public const string ValidCard = "4111111111111111";
    public const string InvalidLuhnCard = "4111111111111112";

    public sealed record Product(string Id, string Name);

    public sealed record CartLine(Product Product, int Quantity);

    public async Task<IReadOnlyList<Product>> ProductsAsync(int count)
    {
        var response = await session.GraphQLAsync("query { products(pageSize: 20) { items { id name } } }");
        Assert.False(response.HasErrors, response.ToString());
        var products = response.Data!["products"]!["items"]!.AsArray()
            .Select(p => new Product((string)p!["id"]!, (string)p["name"]!))
            .Take(count)
            .ToList();
        Assert.True(products.Count == count, $"The environment needs at least {count} products.");
        return products;
    }

    // storeBasket declares $ownerId but the BFF injects it from the session, as for the browser.
    // The price and name sent here are deliberately not the real ones: the checkout must ignore them.
    public async Task StoreCartAsync(IEnumerable<CartLine> lines)
    {
        var items = lines.Select(l => new
        {
            productId = l.Product.Id,
            productName = l.Product.Name,
            price = 0.01m,
            quantity = l.Quantity,
        });
        var response = await session.GraphQLAsync(
            "mutation($ownerId: String!, $input: ShoppingCartInput!) { storeBasket(ownerId: $ownerId, input: $input) { ownerId } }",
            new { ownerId = "", input = new { items } });
        Assert.False(response.HasErrors, response.ToString());
    }

    public enum Payment { Card, Cash }

    // The total the customer is shown for the payment choice: what the checkout must confirm.
    public async Task<decimal> QuoteAsync(IEnumerable<CartLine> lines, Payment payment = Payment.Card)
    {
        var response = await session.GraphQLAsync(
            "query($items: [BasketInstallmentItemInput!]!) { basketInstallmentPlan(items: $items) { price cashPrice } }",
            new { items = lines.Select(l => new { productId = l.Product.Id, quantity = l.Quantity }) });
        Assert.False(response.HasErrors, response.ToString());
        var plan = response.Data!["basketInstallmentPlan"]!;
        return (decimal)plan[payment == Payment.Cash ? "cashPrice" : "price"]!;
    }

    public static Dictionary<string, object?> CheckoutInput(
        decimal totalPrice, string? checkoutId = null, Payment payment = Payment.Card, string cardNumber = ValidCard)
    {
        var input = new Dictionary<string, object?>
        {
            ["totalPrice"] = totalPrice,
            ["firstName"] = "E2E",
            ["lastName"] = "Checkout",
            ["emailAddress"] = "e2e-checkout@duckstore.dev",
            ["addressLine"] = "Rua dos Patos, 42",
            ["country"] = "BR",
            ["state"] = "SP",
            ["zipCode"] = "01000-000",
            ["paymentMethod"] = payment == Payment.Cash ? 3 : 2,
            ["installments"] = 1,
        };
        if (checkoutId is not null)
            input["checkoutId"] = checkoutId;
        if (payment == Payment.Card)
        {
            input["cardName"] = "E2E Checkout";
            input["cardNumber"] = cardNumber;
            input["expiration"] = $"12/{DateTime.UtcNow.Year + 3}";
            input["cvv"] = "123";
        }
        return input;
    }

    public Task<GraphQLResponse> CheckoutAsync(Dictionary<string, object?> input) =>
        session.GraphQLAsync(
            "mutation($input: CheckoutInput!) { checkoutBasket(input: $input) { isSuccess orderId } }",
            new { input });

    public static string OrderIdOf(GraphQLResponse response)
    {
        Assert.False(response.HasErrors, response.ToString());
        var result = response.Data!["checkoutBasket"]!;
        Assert.True((bool)result["isSuccess"]!, response.ToString());
        var orderId = (string)result["orderId"]!;
        Assert.True(Guid.TryParse(orderId, out _), $"orderId '{orderId}' is not a GUID");
        return orderId;
    }
}
