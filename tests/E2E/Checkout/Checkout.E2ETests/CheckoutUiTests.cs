using Microsoft.Playwright;

namespace Checkout.E2ETests;

// The checkout as a customer goes through it in the browser.
public sealed class CheckoutUiTests(StorefrontSession session) : IClassFixture<StorefrontSession>
{
    private readonly Storefront _storefront = new(session);

    private async Task<IPage> CheckoutPageWithACartAsync()
    {
        var products = await _storefront.ProductsAsync(1);
        await _storefront.StoreCartAsync([new(products[0], 1)]);

        var page = await session.NewPageAsync();
        await page.GotoAsync("checkout");
        // The SPA hydrates the cart from the server and quotes it before enabling the button.
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Confirm Order") }))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });
        return page;
    }

    private static async Task FillShippingAsync(IPage page)
    {
        await page.Locator("#name").FillAsync("E2E Checkout");
        await page.Locator("#email").FillAsync("e2e-checkout@duckstore.dev");
        await page.Locator("#address").FillAsync("Rua dos Patos, 42");
        await page.Locator("#city").FillAsync("Sao Paulo");
    }

    private static async Task FillCardAsync(IPage page, string number)
    {
        await page.Locator("#cardNumber").FillAsync(number);
        await page.Locator("#cardExpiry").FillAsync($"12/{(DateTime.UtcNow.Year + 3) % 100:D2}");
        await page.Locator("#cardCvc").FillAsync("123");
    }

    [E2EFact]
    public async Task CustomerPayingByCard_SeesTheOrderNumber()
    {
        var page = await CheckoutPageWithACartAsync();
        await FillShippingAsync(page);
        await FillCardAsync(page, Storefront.ValidCard);

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Confirm Order") }).ClickAsync();

        await Assertions.Expect(page.GetByText("Order Confirmed!")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var orderNumber = await page.GetByTestId("order-number").InnerTextAsync();
        Assert.True(Guid.TryParse(orderNumber, out _), $"order number '{orderNumber}' is not a GUID");
    }

    [E2EFact]
    public async Task CustomerPayingInCash_SeesTheOrderNumber()
    {
        var page = await CheckoutPageWithACartAsync();
        await FillShippingAsync(page);
        await page.Locator("label[for='payment-cash']").ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Confirm Order") }).ClickAsync();

        await Assertions.Expect(page.GetByText("Order Confirmed!")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [E2EFact]
    public async Task CustomerWithAnInvalidCard_IsToldOnTheCardField_AndNoOrderIsPlaced()
    {
        var page = await CheckoutPageWithACartAsync();
        await FillShippingAsync(page);
        await FillCardAsync(page, Storefront.InvalidLuhnCard);

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Confirm Order") }).ClickAsync();

        await Assertions.Expect(page.GetByText("The card number is invalid.")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByText("Order Confirmed!")).ToHaveCountAsync(0);
    }
}
