using Inventory.Function.Modules.Stock.Domain;

namespace Inventory.UnitTests.Modules.Stock.Domain;

public class StorefrontStockTests
{
    private static readonly IReadOnlyDictionary<string, WarehouseState> Warehouses = new Dictionary<string, WarehouseState>
    {
        ["alpha"] = new("alpha", Active: true, Version: 2),
        ["beta"] = new("beta", Active: true, Version: 0),
        ["closed"] = new("closed", Active: false, Version: 1),
    };

    [Fact]
    public void TheTotal_SumsOnlyActiveWarehouses()
    {
        var total = StorefrontStock.TotalOf([new("alpha", 20, 3), new("beta", 50, 1), new("closed", 7, 4)], Warehouses);

        Assert.Equal(70, total.Available);
    }

    [Fact]
    public void AWarehouseThatCannotBeFound_CountsAsInactive()
    {
        var total = StorefrontStock.TotalOf([new("gone", 5, 1)], Warehouses);

        Assert.Equal(0, total.Available);
    }

    [Fact]
    public void Version_SumsTheCountersOfTheBalancesAndTheirWarehouses_InactiveOnesIncluded()
    {
        // Deactivating a warehouse is a change of the total, so its counter counts too.
        var total = StorefrontStock.TotalOf([new("alpha", 1, 3), new("closed", 1, 4)], Warehouses);

        Assert.Equal(3 + 2 + 4 + 1, total.Version);
    }

    public static TheoryData<string, IReadOnlyList<BalanceRow>, IReadOnlyDictionary<string, WarehouseState>> NewerStates => new()
    {
        { "a balance moves", [new("alpha", 25, 4)], Warehouses },
        { "a balance appears in another warehouse", [new("alpha", 20, 3), new("beta", 5, 1)], Warehouses },
        { "its warehouse is deactivated", [new("alpha", 20, 3)], new Dictionary<string, WarehouseState>(Warehouses) { ["alpha"] = new("alpha", false, 3) } },
    };

    [Theory]
    [MemberData(nameof(NewerStates))]
    public void ANewerState_AlwaysHasAGreaterVersion(string change, IReadOnlyList<BalanceRow> balances, IReadOnlyDictionary<string, WarehouseState> warehouses)
    {
        var before = StorefrontStock.TotalOf([new("alpha", 20, 3)], Warehouses);

        var after = StorefrontStock.TotalOf(balances, warehouses);

        Assert.True(after.Version > before.Version, change);
    }

    [Fact]
    public void NoBalanceAtAll_IsZero_AtVersionZero()
    {
        var total = StorefrontStock.TotalOf([], Warehouses);

        Assert.Equal(new StorefrontTotal(0, 0), total);
    }
}
