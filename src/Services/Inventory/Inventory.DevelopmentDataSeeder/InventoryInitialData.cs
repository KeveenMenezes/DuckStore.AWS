namespace Inventory.DevelopmentDataSeeder;

public sealed record SeedWarehouse(
    string Id, string Code, string Name, string Street, string Number, string District, string City,
    string State, string ZipCode, int Priority);

// The two warehouses of the spec's example (product A: 20 in Alpha, 50 in Beta). Fixed ids keep
// local references stable across restarts.
public static class InventoryInitialData
{
    public static IReadOnlyList<SeedWarehouse> Warehouses =>
    [
        new("0f6c3a52-6d0e-4b8e-9a51-3b1d2c4e5f01", "ALPHA", "Polo Alpha",
            "Avenida Paulista", "1000", "Bela Vista", "São Paulo", "SP", "01310100", 1),
        new("0f6c3a52-6d0e-4b8e-9a51-3b1d2c4e5f02", "BETA", "Polo Beta",
            "Rua da Assembleia", "10", "Centro", "Rio de Janeiro", "RJ", "20011000", 2),
    ];
}
