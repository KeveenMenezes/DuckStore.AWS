using Inventory.Function.Modules.Stock.Domain;
using Inventory.Function.Modules.Stock.Domain.Enums;
using Inventory.Function.Modules.Warehouses.Domain;

namespace Inventory.UnitTests.Modules.Stock.Domain;

public class StockBalanceTests
{
    private const string ProductId = "8b0a4c5e-0000-4000-8000-000000000001";
    private static readonly WarehouseSnapshot Alpha = new("w-alpha", "ALPHA", Active: true);
    private static readonly WarehouseSnapshot Beta = new("w-beta", "BETA", Active: true);
    private static readonly WarehouseSnapshot InactiveBeta = Beta with { Active = false };
    private static readonly DateTime At = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static MovementContext Context(Guid? key = null) =>
        new(key ?? Guid.Parse("11111111-2222-4333-8444-555555555555"), "keeper-sub", At, "hash");

    [Fact]
    public void Receive_AddsToTheBalance_AndRecordsOneReceiptRow()
    {
        var balance = StockBalance.Empty(ProductId, Alpha);

        var decision = balance.Receive(20, " 12345 ", " ACME ", null, Context());

        Assert.False(decision.IsRefused);
        Assert.Equal(20, balance.Available);
        var row = Assert.Single(decision.Movements);
        Assert.Equal(StockMovementType.Receipt, row.Type);
        Assert.Equal(20, row.Quantity);
        Assert.Equal(20, row.BalanceAfter);
        Assert.Equal("12345", row.InvoiceNumber);
        Assert.Equal("ACME", row.Supplier);
        Assert.Equal("11111111-2222-4333-8444-555555555555", row.MovementId);
        Assert.Equal("w-alpha", row.WarehouseId);
        Assert.Equal("ALPHA", row.WarehouseCode);
        Assert.Equal("keeper-sub", row.CreatedBy);
        Assert.Equal(At, row.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Receive_WithoutAnInvoice_IsABadRequestOnInvoiceNumber(string? invoice)
    {
        var balance = StockBalance.Empty(ProductId, Alpha);

        var decision = balance.Receive(5, invoice, null, null, Context());

        AssertRefused(decision, "BadRequest", "invoiceNumber");
        Assert.Equal(0, balance.Available);
    }

    [Fact]
    public void Receive_RejectsAnInvoiceOver60_ASupplierOver120_AndANoteOver500()
    {
        AssertRefused(StockBalance.Empty(ProductId, Alpha).Receive(1, new string('x', 61), null, null, Context()), "BadRequest", "invoiceNumber");
        AssertRefused(StockBalance.Empty(ProductId, Alpha).Receive(1, "1", new string('x', 121), null, Context()), "BadRequest", "supplier");
        AssertRefused(StockBalance.Empty(ProductId, Alpha).Receive(1, "1", null, new string('x', 501), Context()), "BadRequest", "note");
        Assert.False(StockBalance.Empty(ProductId, Alpha).Receive(1, new string('x', 60), new string('x', 120), new string('x', 500), Context()).IsRefused);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void EveryMovement_RejectsAQuantityOutside1To100000(int quantity)
    {
        AssertRefused(StockBalance.Empty(ProductId, Alpha).Receive(quantity, "1", null, null, Context()), "BadRequest", "quantity");
        AssertRefused(Balance(Alpha, 10).Adjust(StockAdjustmentDirection.Decrease, quantity, StockAdjustmentReason.Count, null, Context()), "BadRequest", "quantity");
        AssertRefused(Balance(Alpha, 10).TransferTo(StockBalance.Empty(ProductId, Beta), quantity, null, Context()), "BadRequest", "quantity");
    }

    [Fact]
    public void Receive_IntoAnInactiveWarehouse_IsRefused()
    {
        var decision = StockBalance.Empty(ProductId, InactiveBeta).Receive(5, "1", null, null, Context());

        AssertRefused(decision, "WarehouseInactive", "warehouseId");
    }

    [Fact]
    public void Adjust_Decrease_SubtractsAndRecordsTheReason()
    {
        var balance = Balance(Alpha, 20);

        var decision = balance.Adjust(StockAdjustmentDirection.Decrease, 2, StockAdjustmentReason.Damage, "caixa amassada", Context());

        Assert.Equal(18, balance.Available);
        var row = Assert.Single(decision.Movements);
        Assert.Equal(StockMovementType.Adjustment, row.Type);
        Assert.Equal(-2, row.Quantity);
        Assert.Equal(18, row.BalanceAfter);
        Assert.Equal(StockAdjustmentReason.Damage, row.Reason);
        Assert.Equal("caixa amassada", row.Note);
    }

    [Fact]
    public void Adjust_Decrease_BelowZero_IsInsufficientStock_WithTheAvailableQuantity()
    {
        var balance = Balance(Alpha, 18);

        var decision = balance.Adjust(StockAdjustmentDirection.Decrease, 30, StockAdjustmentReason.Count, null, Context());

        AssertRefused(decision, "InsufficientStock", "quantity");
        Assert.Equal(18, decision.Refusal!.Available);
        Assert.Equal(18, balance.Available);
    }

    [Fact]
    public void Adjust_WithReasonOther_RequiresANote()
    {
        AssertRefused(Balance(Alpha, 5).Adjust(StockAdjustmentDirection.Increase, 1, StockAdjustmentReason.Other, "  ", Context()), "BadRequest", "note");
        Assert.False(Balance(Alpha, 5).Adjust(StockAdjustmentDirection.Increase, 1, StockAdjustmentReason.Other, "achado no fundo", Context()).IsRefused);
    }

    [Fact]
    public void Adjust_OnAnInactiveWarehouse_AllowsADecreaseButNotAnIncrease()
    {
        Assert.False(Balance(InactiveBeta, 5).Adjust(StockAdjustmentDirection.Decrease, 5, StockAdjustmentReason.Count, null, Context()).IsRefused);
        AssertRefused(Balance(InactiveBeta, 5).Adjust(StockAdjustmentDirection.Increase, 1, StockAdjustmentReason.Count, null, Context()), "WarehouseInactive", "warehouseId");
    }

    [Fact]
    public void TransferTo_MovesBetweenWarehouses_WithTwoLinkedRows()
    {
        var beta = Balance(Beta, 50);
        var alpha = Balance(Alpha, 18);

        var decision = beta.TransferTo(alpha, 10, "rebalance", Context());

        Assert.Equal(40, beta.Available);
        Assert.Equal(28, alpha.Available);
        Assert.Collection(decision.Movements,
            outRow =>
            {
                Assert.Equal(StockMovementType.TransferOut, outRow.Type);
                Assert.Equal(-10, outRow.Quantity);
                Assert.Equal(40, outRow.BalanceAfter);
                Assert.Equal("w-beta", outRow.WarehouseId);
                Assert.Equal("w-alpha", outRow.CounterpartWarehouseId);
                Assert.Equal("11111111-2222-4333-8444-555555555555#OUT", outRow.MovementId);
            },
            inRow =>
            {
                Assert.Equal(StockMovementType.TransferIn, inRow.Type);
                Assert.Equal(10, inRow.Quantity);
                Assert.Equal(28, inRow.BalanceAfter);
                Assert.Equal("w-alpha", inRow.WarehouseId);
                Assert.Equal("w-beta", inRow.CounterpartWarehouseId);
                Assert.Equal("11111111-2222-4333-8444-555555555555#IN", inRow.MovementId);
            });
        Assert.All(decision.Movements, row => Assert.Equal("11111111-2222-4333-8444-555555555555", row.TransferId));
    }

    [Fact]
    public void TransferTo_TheSameWarehouse_IsABadRequestOnToWarehouseId()
    {
        AssertRefused(Balance(Alpha, 5).TransferTo(StockBalance.Empty(ProductId, Alpha), 1, null, Context()), "BadRequest", "toWarehouseId");
    }

    [Fact]
    public void TransferTo_AnInactiveDestination_IsRefused_ButFromAnInactiveOrigin_IsAllowed()
    {
        AssertRefused(Balance(Alpha, 5).TransferTo(StockBalance.Empty(ProductId, InactiveBeta), 1, null, Context()), "WarehouseInactive", "toWarehouseId");
        Assert.False(Balance(InactiveBeta, 5).TransferTo(Balance(Alpha, 0), 5, null, Context()).IsRefused);
    }

    [Fact]
    public void TransferTo_MoreThanTheOrigin_IsInsufficientStock_AndNothingMoves()
    {
        var beta = Balance(Beta, 3);
        var alpha = Balance(Alpha, 0);

        var decision = beta.TransferTo(alpha, 4, null, Context());

        AssertRefused(decision, "InsufficientStock", "quantity");
        Assert.Equal(3, decision.Refusal!.Available);
        Assert.Equal(3, beta.Available);
        Assert.Equal(0, alpha.Available);
    }

    [Fact]
    public void TheLedger_AlwaysAddsUpToTheBalance()
    {
        // The invariant of ADR-0052 §7: per product × warehouse, Σ Quantity == Available.
        var alpha = StockBalance.Empty(ProductId, Alpha);
        var beta = StockBalance.Empty(ProductId, Beta);
        var rows = new List<StockMovement>();

        rows.AddRange(alpha.Receive(20, "NF-1", null, null, Context(Guid.NewGuid())).Movements);
        rows.AddRange(beta.Receive(50, "NF-2", null, null, Context(Guid.NewGuid())).Movements);
        rows.AddRange(alpha.Adjust(StockAdjustmentDirection.Decrease, 2, StockAdjustmentReason.Damage, null, Context(Guid.NewGuid())).Movements);
        rows.AddRange(beta.TransferTo(alpha, 10, null, Context(Guid.NewGuid())).Movements);
        rows.AddRange(alpha.Adjust(StockAdjustmentDirection.Increase, 5, StockAdjustmentReason.Count, null, Context(Guid.NewGuid())).Movements);

        Assert.Equal(alpha.Available, rows.Where(r => r.WarehouseId == "w-alpha").Sum(r => r.Quantity));
        Assert.Equal(beta.Available, rows.Where(r => r.WarehouseId == "w-beta").Sum(r => r.Quantity));
        Assert.Equal(33, alpha.Available);
        Assert.Equal(40, beta.Available);
        // Each row's BalanceAfter is the running sum of its warehouse.
        foreach (var warehouse in new[] { "w-alpha", "w-beta" })
        {
            var running = 0;
            foreach (var row in rows.Where(r => r.WarehouseId == warehouse))
            {
                running += row.Quantity;
                Assert.Equal(running, row.BalanceAfter);
            }
        }
    }

    [Fact]
    public void Load_KeepsTheReadValue_ForTheOptimisticCondition()
    {
        var loaded = Balance(Alpha, 7);
        var fresh = StockBalance.Empty(ProductId, Alpha);

        loaded.Receive(1, "1", null, null, Context());

        Assert.Equal(7, loaded.ReadAvailable);
        Assert.Equal(8, loaded.Available);
        Assert.Null(fresh.ReadAvailable);
    }

    [Fact]
    public void BalanceAfter_IsTheStockOnHand_AndOnlyAvailableCanBeTakenOut()
    {
        // Reserved units are still on hand, so the ledger counts them; they just can't be adjusted away.
        var balance = StockBalance.Load(ProductId, Alpha, available: 5, reserved: 2);

        var receipt = balance.Receive(1, "1", null, null, Context(Guid.NewGuid()));
        var tooMuch = balance.Adjust(StockAdjustmentDirection.Decrease, 7, StockAdjustmentReason.Loss, null, Context(Guid.NewGuid()));

        Assert.Equal(8, Assert.Single(receipt.Movements).BalanceAfter);
        Assert.Equal(6, tooMuch.Refusal!.Available);
    }

    private static StockBalance Balance(WarehouseSnapshot warehouse, int available) =>
        StockBalance.Load(ProductId, warehouse, available, reserved: 0);

    private static void AssertRefused(MovementDecision decision, string errorType, string field)
    {
        Assert.True(decision.IsRefused, "expected a refusal");
        Assert.Equal(errorType, decision.Refusal!.ErrorType);
        Assert.Equal(field, decision.Refusal.Field);
        Assert.Empty(decision.Movements);
    }
}
