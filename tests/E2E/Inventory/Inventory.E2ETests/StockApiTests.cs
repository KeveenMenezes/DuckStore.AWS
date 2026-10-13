namespace Inventory.E2ETests;

// Stock movements and views (ADR-0052 §7) on the deployed AppSync, as a StockKeeper, on a real Catalog
// product. Each test works in its own E2E- warehouses and deactivates them at the end; the movements
// stay in the immutable ledger of those inactive warehouses.
public sealed class StockApiTests(StockKeeperSession session) : IClassFixture<StockKeeperSession>
{
    private readonly WarehouseApi _warehouses = new(session);
    private readonly StockApi _stock = new(session);

    private async Task<(string A, string B)> TwoWarehousesAsync()
    {
        var a = await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 900);
        var b = await _warehouses.CreateValidAsync(WarehouseApi.NewCode(), priority: 901);
        return ((string)a["id"]!, (string)b["id"]!);
    }

    private Task DeactivateAsync(params string[] ids) => Task.WhenAll(ids.Select(id => _warehouses.SetActiveAsync(id, false)));

    [ManagementE2EFact]
    public async Task ReceiveAdjustAndTransfer_MoveTheBalances_AndTheLedgerAddsUp()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var (a, b) = await TwoWarehousesAsync();
        try
        {
            var receipt = await _stock.ReceiveAsync(productId, a, 20, invoice: "12345", supplier: "ACME");
            var row = Assert.Single(StockApi.MovementsOf(receipt, "recordStockReceipt"));
            Assert.Equal("Receipt", (string?)row!["type"]);
            Assert.Equal(20, (int)row["balanceAfter"]!);
            Assert.Equal("12345", (string?)row["invoiceNumber"]);
            Assert.Equal("ACME", (string?)row["supplier"]);
            Assert.False(string.IsNullOrEmpty((string?)row["createdBy"]));

            await _stock.ReceiveAsync(productId, b, 50);
            var damaged = await _stock.AdjustAsync(productId, a, "Decrease", 2, "Damage", "caixa amassada");
            Assert.Equal(-2, (int)StockApi.MovementsOf(damaged, "recordStockAdjustment")[0]!["quantity"]!);

            var transfer = await _stock.TransferAsync(productId, b, a, 10);
            var rows = StockApi.MovementsOf(transfer, "transferStock");
            Assert.Equal(["TransferOut", "TransferIn"], rows.Select(r => (string?)r!["type"]));
            Assert.Single(rows.Select(r => (string?)r!["transferId"]).Distinct());

            Assert.Equal(28, await _stock.AvailableAsync(productId, a));
            Assert.Equal(40, await _stock.AvailableAsync(productId, b));
            Assert.Equal(28, await _stock.LedgerSumAsync(productId, a));
            Assert.Equal(40, await _stock.LedgerSumAsync(productId, b));
        }
        finally
        {
            await DeactivateAsync(a, b);
        }
    }

    [ManagementE2EFact]
    public async Task InvalidMovements_AreTypedRefusals_AndWriteNothing()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var (a, b) = await TwoWarehousesAsync();
        try
        {
            await _stock.ReceiveAsync(productId, a, 18);

            var noInvoice = await _stock.ReceiveAsync(productId, a, 1, invoice: " ");
            Assert.Equal("BadRequest", noInvoice.ErrorType);
            Assert.Equal("invoiceNumber", (string?)noInvoice.ErrorInfo?["field"]);

            var otherWithoutNote = await _stock.AdjustAsync(productId, a, "Increase", 1, "Other");
            Assert.Equal("note", (string?)otherWithoutNote.ErrorInfo?["field"]);

            var tooMuch = await _stock.AdjustAsync(productId, a, "Decrease", 30);
            Assert.Equal("InsufficientStock", tooMuch.ErrorType);
            Assert.Equal(18, (int?)tooMuch.ErrorInfo?["available"]);

            var tooMuchTransfer = await _stock.TransferAsync(productId, a, b, 19);
            Assert.Equal("InsufficientStock", tooMuchTransfer.ErrorType);

            var toItself = await _stock.TransferAsync(productId, a, a, 1);
            Assert.Equal("toWarehouseId", (string?)toItself.ErrorInfo?["field"]);

            var ghostProduct = await _stock.ReceiveAsync(Guid.NewGuid().ToString(), a, 1);
            Assert.Equal("ProductNotFound", ghostProduct.ErrorType);

            var ghostWarehouse = await _stock.ReceiveAsync(productId, Guid.NewGuid().ToString(), 1);
            Assert.Equal("WarehouseNotFound", ghostWarehouse.ErrorType);

            Assert.Equal(18, await _stock.AvailableAsync(productId, a));
            Assert.Equal(18, await _stock.LedgerSumAsync(productId, a));
        }
        finally
        {
            await DeactivateAsync(a, b);
        }
    }

    [ManagementE2EFact]
    public async Task AnInactiveWarehouse_OnlyGivesStockOut()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var (a, b) = await TwoWarehousesAsync();
        try
        {
            await _stock.ReceiveAsync(productId, b, 10);
            await _warehouses.SetActiveAsync(b, false);

            Assert.Equal("WarehouseInactive", (await _stock.ReceiveAsync(productId, b, 1)).ErrorType);
            Assert.Equal("WarehouseInactive", (await _stock.AdjustAsync(productId, b, "Increase", 1)).ErrorType);
            Assert.Equal("WarehouseInactive", (await _stock.TransferAsync(productId, a, b, 1)).ErrorType);

            Assert.False((await _stock.AdjustAsync(productId, b, "Decrease", 2, "Loss")).HasErrors);
            Assert.False((await _stock.TransferAsync(productId, b, a, 8)).HasErrors);
            Assert.Equal(0, await _stock.AvailableAsync(productId, b));
            Assert.Equal(8, await _stock.AvailableAsync(productId, a));
        }
        finally
        {
            await DeactivateAsync(a, b);
        }
    }

    [ManagementE2EFact]
    public async Task ARetryWithTheSameKey_IsRecordedOnce_EvenWhenConcurrent()
    {
        var (productId, _) = await _stock.AnyProductAsync();
        var (a, _) = await TwoWarehousesAsync();
        try
        {
            var key = Guid.NewGuid().ToString();
            var first = await _stock.ReceiveAsync(productId, a, 5, key: key);
            var replay = await _stock.ReceiveAsync(productId, a, 5, key: key);
            Assert.Equal((string?)StockApi.MovementsOf(first, "recordStockReceipt")[0]!["id"],
                (string?)StockApi.MovementsOf(replay, "recordStockReceipt")[0]!["id"]);

            var reused = await _stock.ReceiveAsync(productId, a, 6, key: key);
            Assert.Equal("IdempotencyKeyReused", reused.ErrorType);

            // A double click: two requests of the same attempt racing through the Lambda.
            var twin = Guid.NewGuid().ToString();
            var raced = await Task.WhenAll(_stock.ReceiveAsync(productId, a, 3, key: twin), _stock.ReceiveAsync(productId, a, 3, key: twin));
            Assert.All(raced, r => Assert.False(r.HasErrors, r.ToString()));

            // Different attempts racing on one balance: every one lands, none overwrites another.
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _stock.ReceiveAsync(productId, a, 1)));
            var recorded = concurrent.Count(r => !r.HasErrors);

            Assert.Equal(8 + recorded, await _stock.AvailableAsync(productId, a));
            Assert.Equal(8 + recorded, await _stock.LedgerSumAsync(productId, a));
        }
        finally
        {
            await DeactivateAsync(a);
        }
    }

    [ManagementE2EFact]
    public async Task TheWarehouseViews_ListItsBalances_AndHistory_WithTheProductName()
    {
        var (productId, productName) = await _stock.AnyProductAsync();
        var (a, _) = await TwoWarehousesAsync();
        try
        {
            await _stock.ReceiveAsync(productId, a, 7);
            await _stock.AdjustAsync(productId, a, "Decrease", 1, "Count");

            var balances = await _stock.StockByWarehouseAsync(a);
            var balance = Assert.Single(balances.Data!["stockByWarehouse"]!["items"]!.AsArray());
            Assert.Equal(productName, (string?)balance!["productName"]);
            Assert.Equal(6, (int)balance["available"]!);

            var history = (await _stock.MovementsByWarehouseAsync(a)).Data!["stockMovementsByWarehouse"]!["items"]!.AsArray();
            Assert.Equal(["Adjustment", "Receipt"], history.Select(m => (string?)m!["type"]));
            Assert.All(history, m => Assert.Equal(productName, (string?)m!["productName"]));

            var badPage = await _stock.StockByWarehouseAsync(a, pageSize: 101);
            Assert.Equal("BadRequest", badPage.ErrorType);
        }
        finally
        {
            await DeactivateAsync(a);
        }
    }
}

// A Seller's token is refused by every stock operation, reads included.
public sealed class SellerStockApiTests(SellerSession session) : IClassFixture<SellerSession>
{
    private readonly StockApi _stock = new(session);

    [ManagementE2EFact]
    public async Task EveryStockOperation_IsUnauthorized()
    {
        var id = Guid.NewGuid().ToString();
        var responses = new[]
        {
            await _stock.StockByProductAsync(id),
            await _stock.StockByWarehouseAsync(id),
            await _stock.MovementsByProductAsync(id),
            await _stock.MovementsByWarehouseAsync(id),
            await _stock.ReceiveAsync(id, id, 1),
            await _stock.AdjustAsync(id, id, "Decrease", 1),
            await _stock.TransferAsync(id, id, Guid.NewGuid().ToString(), 1),
        };

        Assert.All(responses, response => Assert.Equal("Unauthorized", response.ErrorType));
    }
}

// A storefront customer is refused too, through the SPA's BFF.
public sealed class CustomerStockApiTests(StorefrontSession session) : IClassFixture<StorefrontSession>
{
    [ManagementE2EFact(requiresStorefront: true)]
    public async Task StockByProduct_IsUnauthorizedForACustomer()
    {
        var response = await session.GraphQLAsync(
            "{ stockByProduct(productId: \"00000000-0000-0000-0000-000000000000\") { totalAvailable } }");

        Assert.Equal("Unauthorized", response.ErrorType);
    }
}
