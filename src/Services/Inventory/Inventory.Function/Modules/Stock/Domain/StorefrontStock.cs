namespace Inventory.Function.Modules.Stock.Domain;

// One balance row as the storefront total sees it.
public sealed record BalanceRow(string WarehouseId, int Available, string UpdatedAt);

// What the storefront total needs to know about a warehouse.
public sealed record WarehouseState(string Id, bool Active, string UpdatedAt);

public sealed record StorefrontTotal(int Available, string AsOf);

// The number the storefront shows for a product (ADR-0052 §8): Available summed over its balances in
// active warehouses. AsOf is the latest update among those balances and their warehouses — inactive
// ones included, since deactivating one changes the total — so recomputing from a newer state never
// yields an earlier AsOf, and a consumer can apply totals in any order of delivery.
public static class StorefrontStock
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static StorefrontTotal TotalOf(
        IReadOnlyList<BalanceRow> balances, IReadOnlyDictionary<string, WarehouseState> warehouses)
    {
        var available = 0;
        DateTime? asOf = null;

        foreach (var balance in balances)
        {
            asOf = Latest(asOf, balance.UpdatedAt);
            if (!warehouses.TryGetValue(balance.WarehouseId, out var warehouse))
                continue;

            asOf = Latest(asOf, warehouse.UpdatedAt);
            if (warehouse.Active)
                available += balance.Available;
        }

        return new StorefrontTotal(available, asOf?.ToString(Format, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    // Writers use different precisions (the seeder seven fractional digits, AppSync and the Lambdas
    // three), so instants are compared, not strings, and AsOf is written in one fixed format.
    private static DateTime? Latest(DateTime? current, string timestamp)
    {
        if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant))
            return current;
        instant = new DateTime(instant.Ticks - instant.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        return current is null || instant > current ? instant : current;
    }
}
