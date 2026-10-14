namespace Inventory.Function.Modules.Stock.Domain;

// One balance row as the storefront total sees it. Version counts the row's writes.
public sealed record BalanceRow(string WarehouseId, int Available, long Version);

// What the storefront total needs to know about a warehouse. Version counts its activation changes.
public sealed record WarehouseState(string Id, bool Active, long Version);

public sealed record StorefrontTotal(int Available, long Version);

// The number the storefront shows for a product (ADR-0052 §8): Available summed over its balances in
// active warehouses. Version is the sum of the counters of those balances and their warehouses —
// inactive ones included, since deactivating one changes the total. Every write that can change the
// total adds one to a counter in that sum, and none is ever taken away, so recomputing from a newer
// state always yields a greater Version, and a consumer can apply totals in any order of delivery.
public static class StorefrontStock
{
    public static StorefrontTotal TotalOf(
        IReadOnlyList<BalanceRow> balances, IReadOnlyDictionary<string, WarehouseState> warehouses)
    {
        var available = 0;
        var version = 0L;

        // A product has one row per warehouse, so each warehouse's counter is added once.
        foreach (var balance in balances)
        {
            version += balance.Version;
            if (!warehouses.TryGetValue(balance.WarehouseId, out var warehouse))
                continue;

            version += warehouse.Version;
            if (warehouse.Active)
                available += balance.Available;
        }

        return new StorefrontTotal(available, version);
    }
}
