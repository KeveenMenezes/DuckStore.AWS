using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Inventory.E2ETests;

// The Warehouses pages as a StockKeeper uses them (SPEC-inventory-warehouses criteria 1, 2, 4, 5, 6).
public sealed class StockKeeperUiTests(StockKeeperSession session) : IClassFixture<StockKeeperSession>
{
    private readonly WarehouseApi _api = new(session);
    private IPage Page => session.Page;

    [ManagementE2EFact]
    public async Task Menu_ShowsWarehouses()
    {
        await session.GotoAsync("");

        await Expect(Page.Locator(".sidebar-nav").GetByRole(AriaRole.Link, new() { Name = "Warehouses" })).ToBeVisibleAsync();
    }

    [ManagementE2EFact]
    public async Task CreateEditDeactivateAndReactivate_ThroughThePages()
    {
        var code = WarehouseApi.NewCode();
        string? id = null;
        try
        {
            await session.GotoAsync("warehouses/new");
            await Page.Locator("#code").FillAsync(code.ToLowerInvariant());
            await Page.Locator("#name").FillAsync("E2E UI warehouse");
            await Page.Locator("#street").FillAsync("Avenida Paulista");
            await Page.Locator("#number").FillAsync("1000");
            await Page.Locator("#district").FillAsync("Bela Vista");
            await Page.Locator("#city").FillAsync("São Paulo");
            await Page.Locator("#state").SelectOptionAsync("SP");
            await Page.Locator("#zipCode").FillAsync("01310-100");
            await Page.Locator("#priority").FillAsync("600");
            await session.CaptureAsync("warehouse-form");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

            // Back on the list, normalized and active (criterion 2).
            var row = Page.Locator($"tr[data-code='{code}']");
            await Expect(row).ToBeVisibleAsync();
            await Expect(row.Locator(".warehouse-status")).ToHaveTextAsync("Active");
            await Expect(Page.Locator(".banner-success")).ToContainTextAsync(code);
            id = (string?)(await _api.ListAsync()).Data!["warehouses"]!.AsArray()
                .Single(w => (string?)w!["code"] == code)!["id"];
            await session.CaptureAsync("warehouse-created");

            // Edit: the code is read-only, the priority changes (criterion 5).
            await row.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
            await Expect(Page.Locator("#code")).ToBeDisabledAsync();
            await Page.Locator("#priority").FillAsync("650");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            await Expect(row.Locator("td").Nth(3)).ToHaveTextAsync("650");

            // Deactivate and reactivate; it stays listed (criterion 6).
            await row.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();
            await Expect(row.Locator(".warehouse-status")).ToHaveTextAsync("Inactive");
            await session.CaptureAsync("warehouse-inactive");
            await row.GetByRole(AriaRole.Button, new() { Name = "Activate" }).ClickAsync();
            await Expect(row.Locator(".warehouse-status")).ToHaveTextAsync("Active");
        }
        finally
        {
            if (id is not null)
                await _api.SetActiveAsync(id, false);
        }
    }

    [ManagementE2EFact]
    public async Task ATakenCode_IsShownNextToTheCodeField()
    {
        var code = WarehouseApi.NewCode();
        var existing = await _api.CreateValidAsync(code);
        try
        {
            await session.GotoAsync("warehouses/new");
            await Page.Locator("#code").FillAsync(code);
            await Page.Locator("#name").FillAsync("E2E duplicate");
            await Page.Locator("#street").FillAsync("Rua A");
            await Page.Locator("#number").FillAsync("1");
            await Page.Locator("#district").FillAsync("Centro");
            await Page.Locator("#city").FillAsync("Recife");
            await Page.Locator("#state").SelectOptionAsync("PE");
            await Page.Locator("#zipCode").FillAsync("50000000");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

            // The resolver's WarehouseCodeTaken, rendered at errorInfo.field (criterion 3/4).
            await Expect(Page.Locator(".server-error[data-field='code']")).ToContainTextAsync("already taken");
            await session.CaptureAsync("warehouse-code-taken");
        }
        finally
        {
            await _api.SetActiveAsync((string)existing["id"]!, false);
        }
    }

    [ManagementE2EFact]
    public async Task AnInvalidZipCode_IsCaughtByTheFormBeforeSubmitting()
    {
        await session.GotoAsync("warehouses/new");
        await Page.Locator("#code").FillAsync(WarehouseApi.NewCode());
        await Page.Locator("#zipCode").FillAsync("0131010");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Expect(Page.GetByText("Zip code must have 8 digits (00000-000).")).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("warehouses/new$"));
    }
}

// Criterion 1: Admin sees and uses the Warehouses pages like a StockKeeper.
public sealed class AdminUiTests(AdminSession session) : IClassFixture<AdminSession>
{
    [ManagementE2EFact]
    public async Task Menu_ShowsWarehouses_AndTheListLoads()
    {
        await session.GotoAsync("warehouses");

        await Expect(session.Page.Locator(".sidebar-nav").GetByRole(AriaRole.Link, new() { Name = "Warehouses" })).ToBeVisibleAsync();
        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "New warehouse" })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Loading warehouses...")).ToBeHiddenAsync();
        await Expect(session.Page.Locator(".banner-error")).ToHaveCountAsync(0);
    }
}

// Criterion 1: a Seller has no Warehouses menu, and the page itself refuses.
public sealed class SellerUiTests(SellerSession session) : IClassFixture<SellerSession>
{
    [ManagementE2EFact]
    public async Task Menu_HidesWarehouses_AndThePageRefuses()
    {
        await session.GotoAsync("");
        await Expect(session.Page.Locator(".sidebar-nav").GetByRole(AriaRole.Link, new() { Name = "Products" })).ToBeVisibleAsync();
        await Expect(session.Page.Locator(".sidebar-nav").GetByRole(AriaRole.Link, new() { Name = "Warehouses" })).ToHaveCountAsync(0);

        await session.GotoAsync("warehouses");
        await Expect(session.Page.GetByText("Only the StockKeeper and Admin groups can manage warehouses.")).ToBeVisibleAsync();
        await session.CaptureAsync("seller-no-access");
    }
}
