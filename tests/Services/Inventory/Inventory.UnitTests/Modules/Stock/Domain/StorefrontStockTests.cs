using Inventory.Function.Modules.Stock.Domain;

namespace Inventory.UnitTests.Modules.Stock.Domain;

public class StorefrontStockTests
{
    private static readonly IReadOnlyDictionary<string, WarehouseState> Warehouses = new Dictionary<string, WarehouseState>
    {
        ["alpha"] = new("alpha", Active: true, "2026-10-13T10:00:00.000Z"),
        ["beta"] = new("beta", Active: true, "2026-10-13T09:00:00.000Z"),
        ["closed"] = new("closed", Active: false, "2026-10-13T11:00:00.000Z"),
    };

    [Fact]
    public void TheTotal_SumsOnlyActiveWarehouses()
    {
        var total = StorefrontStock.TotalOf(
            [new("alpha", 20, "2026-10-13T12:00:00.000Z"), new("beta", 50, "2026-10-13T12:01:00.000Z"), new("closed", 7, "2026-10-13T12:02:00.000Z")],
            Warehouses);

        Assert.Equal(70, total.Available);
    }

    [Fact]
    public void AWarehouseThatCannotBeFound_CountsAsInactive()
    {
        var total = StorefrontStock.TotalOf([new("gone", 5, "2026-10-13T12:00:00.000Z")], Warehouses);

        Assert.Equal(0, total.Available);
    }

    [Fact]
    public void AsOf_IsTheLatestUpdate_AmongTheBalancesAndTheirWarehouses_InactiveOnesIncluded()
    {
        // Deactivating a warehouse is a change of the total, so its time counts too.
        var total = StorefrontStock.TotalOf(
            [new("alpha", 1, "2026-10-13T08:00:00.000Z"), new("closed", 1, "2026-10-13T08:30:00.000Z")], Warehouses);

        Assert.Equal("2026-10-13T11:00:00.000Z", total.AsOf);
    }

    [Fact]
    public void AsOf_ComparesInstants_NotTheTextOfDifferentFormats()
    {
        // The seeder writes seven fractional digits, AppSync and the Lambdas three.
        var total = StorefrontStock.TotalOf(
            [new("alpha", 1, "2026-10-13T12:00:00.9990000Z"), new("beta", 1, "2026-10-13T12:00:01.000Z")], Warehouses);

        Assert.Equal("2026-10-13T12:00:01.000Z", total.AsOf);
    }

    [Fact]
    public void ANewerState_NeverProducesAnEarlierAsOf()
    {
        var before = StorefrontStock.TotalOf([new("alpha", 20, "2026-10-13T12:00:00.000Z")], Warehouses);
        var deactivated = new Dictionary<string, WarehouseState>(Warehouses)
        {
            ["alpha"] = new("alpha", Active: false, "2026-10-13T12:00:05.000Z"),
        };

        var after = StorefrontStock.TotalOf([new("alpha", 20, "2026-10-13T12:00:00.000Z")], deactivated);

        Assert.Equal(0, after.Available);
        Assert.True(string.CompareOrdinal(after.AsOf, before.AsOf) > 0);
    }

    [Fact]
    public void NoBalanceAtAll_IsZero_WithAnEmptyAsOf()
    {
        var total = StorefrontStock.TotalOf([], Warehouses);

        Assert.Equal(0, total.Available);
        Assert.Equal(string.Empty, total.AsOf);
    }
}
