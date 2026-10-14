using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Inventory.E2ETests;

// The storefront's stock is Inventory's total over active warehouses (ADR-0052 §8), on the deployed
// stream publishers, EventBridge and catalogview-inventory-sync-consumer. The movements happen in
// fresh E2E- warehouses on a real product; deactivating them at the end gives the product its number
// back.
public sealed class StorefrontStockTests(StockKeeperSession session) : IClassFixture<StockKeeperSession>
{
    private readonly WarehouseApi _warehouses = new(session);
    private readonly StockApi _stock = new(session);

    // The tests before this one move the same product and deactivate their warehouses at the end; their
    // last totals may still be on their way. Start from the storefront once it shows Inventory's own
    // total over active warehouses.
    private async Task<int> SettledStorefrontStockAsync(string productId)
    {
        var active = (await _warehouses.ListAsync()).Data!["warehouses"]!.AsArray()
            .Where(w => (bool)w!["active"]!).Select(w => (string)w!["id"]!).ToHashSet();
        var expected = (await _stock.StockByProductAsync(productId)).Data!["stockByProduct"]!["warehouses"]!.AsArray()
            .Where(row => active.Contains((string)row!["warehouseId"]!)).Sum(row => (int)row!["available"]!);
        var shown = await _stock.WaitForShownStockAsync(productId, expected);
        Assert.Equal(expected, shown);
        return shown;
    }

    [ManagementE2EFact]
    public async Task TheStorefront_FollowsTheTotalOverActiveWarehouses()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var a = (string)(await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 900))["id"]!;
        var b = (string)(await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 901))["id"]!;
        var shown = await SettledStorefrontStockAsync(productId);
        try
        {
            Assert.False((await _stock.ReceiveAsync(productId, a, 5)).HasErrors);
            Assert.Equal(shown + 5, await _stock.WaitForShownStockAsync(productId, shown + 5));

            Assert.False((await _stock.ReceiveAsync(productId, b, 7)).HasErrors);
            Assert.False((await _stock.TransferAsync(productId, a, b, 2)).HasErrors);
            // A transfer between two active warehouses leaves the total alone: 5 + 7.
            Assert.Equal(shown + 12, await _stock.WaitForShownStockAsync(productId, shown + 12));

            // b holds 9 now: deactivating it takes them off the storefront, reactivating brings them back.
            await _warehouses.SetActiveAsync(b, false);
            Assert.Equal(shown + 3, await _stock.WaitForShownStockAsync(productId, shown + 3));
            await _warehouses.SetActiveAsync(b, true);
            Assert.Equal(shown + 12, await _stock.WaitForShownStockAsync(productId, shown + 12));

            // Two quick movements: whatever order their totals arrive in, the storefront ends on the last.
            await Task.WhenAll(_stock.AdjustAsync(productId, a, "Decrease", 1), _stock.AdjustAsync(productId, b, "Decrease", 1));
            Assert.Equal(shown + 10, await _stock.WaitForShownStockAsync(productId, shown + 10));
        }
        finally
        {
            await Task.WhenAll(_warehouses.SetActiveAsync(a, false), _warehouses.SetActiveAsync(b, false));
        }

        Assert.Equal(shown, await _stock.WaitForShownStockAsync(productId, shown));
    }
}

// The Seller no longer types stock: the product form has no such field (ADR-0052 §8).
public sealed class SellerProductFormTests(SellerSession session) : IClassFixture<SellerSession>
{
    [ManagementE2EFact]
    public async Task TheProductForm_HasNoStockField()
    {
        await session.GotoAsync("products/new");

        await Expect(session.Page.Locator("#name")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(session.Page.Locator("#stock")).ToHaveCountAsync(0);
    }
}
