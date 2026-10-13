using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Inventory.E2ETests;

// The Stock pages as a StockKeeper uses them (ADR-0052 §7): receipt, adjustment and transfer from the
// product page, the history, and the warehouse stock page. Each test works in its own E2E-
// warehouses and deactivates them at the end.
public sealed class StockKeeperStockUiTests(StockKeeperSession session) : IClassFixture<StockKeeperSession>
{
    private readonly WarehouseApi _warehouses = new(session);
    private readonly StockApi _stock = new(session);
    private IPage Page => session.Page;

    private ILocator Balance(string code) => Page.Locator($"tr[data-warehouse-code='{code}'] .stock-available");

    private ILocator FormPanel => Page.Locator(".stock-form-panel");

    [ManagementE2EFact]
    public async Task Menu_ShowsStock_AndTheSearchFindsAProduct()
    {
        var (productId, productName) = await _stock.AnyProductAsync();

        await session.GotoAsync("stock");
        await Expect(Page.Locator(".sidebar-nav").GetByRole(AriaRole.Link, new() { Name = "Stock" })).ToBeVisibleAsync();
        await Page.Locator("#stock-search").FillAsync(productName);
        await Page.Locator("#stock-search").PressAsync("Enter");

        await Expect(Page.Locator($"tr[data-product-id='{productId}']").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [ManagementE2EFact]
    public async Task ReceiveAdjustAndTransfer_FromTheProductPage()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var a = await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 900);
        var b = await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 901);
        var (aId, aCode) = ((string)a["id"]!, (string)a["code"]!);
        var (bId, bCode) = ((string)b["id"]!, (string)b["code"]!);
        try
        {
            await session.GotoAsync($"stock/products/{productId}");

            // Receipt of 12 into A, with an invoice.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Receive", Exact = true }).ClickAsync();
            await Page.Locator("#receipt-warehouse").SelectOptionAsync(aId);
            await Page.Locator("#receipt-quantity").FillAsync("12");
            await Page.Locator("#receipt-invoice").FillAsync("NF-UI-1");
            await Page.Locator("#receipt-supplier").FillAsync("ACME");
            await session.CaptureAsync("stock-receipt-form");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Record receipt" }).ClickAsync();
            await Expect(Balance(aCode)).ToHaveTextAsync("12");

            // Adjustment: −2 for damage.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Adjust", Exact = true }).ClickAsync();
            await Page.Locator("#adjust-direction").SelectOptionAsync("Decrease");
            await Page.Locator("#adjust-warehouse").SelectOptionAsync(aId);
            await Page.Locator("#adjust-quantity").FillAsync("2");
            await Page.Locator("#adjust-reason").SelectOptionAsync("Damage");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Record adjustment" }).ClickAsync();
            await Expect(Balance(aCode)).ToHaveTextAsync("10");

            // Transfer 4 from A to B.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Transfer", Exact = true }).First.ClickAsync();
            await Page.Locator("#transfer-from").SelectOptionAsync(aId);
            await Page.Locator("#transfer-to").SelectOptionAsync(bId);
            await Page.Locator("#transfer-quantity").FillAsync("4");
            await FormPanel.GetByRole(AriaRole.Button, new() { Name = "Transfer", Exact = true }).ClickAsync();
            await Expect(Balance(aCode)).ToHaveTextAsync("6");
            await Expect(Balance(bCode)).ToHaveTextAsync("4");
            await session.CaptureAsync("stock-after-transfer");

            // The history shows every row, newest first.
            await Expect(Page.Locator("td.movement-type", new() { HasTextString = "Transfer in" }).First).ToBeVisibleAsync();
            await Expect(Page.Locator("td.movement-type", new() { HasTextString = "Transfer out" }).First).ToBeVisibleAsync();
            Assert.Equal(6, await _stock.LedgerSumAsync(productId, aId));
        }
        finally
        {
            await Task.WhenAll(_warehouses.SetActiveAsync(aId, false), _warehouses.SetActiveAsync(bId, false));
        }
    }

    [ManagementE2EFact]
    public async Task TakingOutMoreThanAvailable_ShowsTheAvailableQuantity_NextToTheField()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var a = await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 900);
        var aId = (string)a["id"]!;
        try
        {
            await _stock.ReceiveAsync(productId, aId, 3);
            await session.GotoAsync($"stock/products/{productId}");

            await Page.GetByRole(AriaRole.Button, new() { Name = "Adjust", Exact = true }).ClickAsync();
            await Page.Locator("#adjust-direction").SelectOptionAsync("Decrease");
            await Page.Locator("#adjust-warehouse").SelectOptionAsync(aId);
            await Page.Locator("#adjust-quantity").FillAsync("9");
            await Page.Locator("#adjust-reason").SelectOptionAsync("Loss");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Record adjustment" }).ClickAsync();

            await Expect(Page.Locator(".server-error[data-field='quantity']")).ToContainTextAsync("3");
            await session.CaptureAsync("stock-insufficient");
        }
        finally
        {
            await _warehouses.SetActiveAsync(aId, false);
        }
    }

    [ManagementE2EFact]
    public async Task TheWarehouseStockPage_ListsItsProducts_AndHistory()
    {
        var (productId, productName) = await _stock.AnyProductAsync();
        var a = await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 900);
        var aId = (string)a["id"]!;
        try
        {
            await _stock.ReceiveAsync(productId, aId, 5);

            await session.GotoAsync("warehouses");
            await Page.Locator($"tr[data-code='{(string)a["code"]!}']").GetByRole(AriaRole.Link, new() { Name = "Stock" }).ClickAsync();

            var row = Page.Locator($"tr[data-product-id='{productId}']").First;
            await Expect(row).ToBeVisibleAsync();
            await Expect(row).ToContainTextAsync(productName);
            await Expect(Page.Locator("td.movement-type", new() { HasTextString = "Receipt" }).First).ToBeVisibleAsync();
            await session.CaptureAsync("warehouse-stock");
        }
        finally
        {
            await _warehouses.SetActiveAsync(aId, false);
        }
    }
}

// A Seller has no Stock menu, and the page itself refuses.
public sealed class SellerStockUiTests(SellerSession session) : IClassFixture<SellerSession>
{
    [ManagementE2EFact]
    public async Task Menu_HidesStock_AndThePageRefuses()
    {
        await session.GotoAsync("");
        await Expect(session.Page.Locator(".sidebar-nav").GetByRole(AriaRole.Link, new() { Name = "Stock" })).ToHaveCountAsync(0);

        await session.GotoAsync("stock");
        await Expect(session.Page.GetByText("Only the StockKeeper and Admin groups can manage stock.")).ToBeVisibleAsync();
    }
}
