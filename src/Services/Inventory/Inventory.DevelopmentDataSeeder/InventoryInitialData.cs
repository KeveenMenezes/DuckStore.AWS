namespace Inventory.DevelopmentDataSeeder;

public sealed record SeedReceipt(string ProductId, int Quantity);

public sealed record SeedWarehouse(
    string Id, string Code, string Name, string Street, string Number, string District, string City,
    string State, string ZipCode, int Priority);

// Two warehouses, as in ADR-0052's example (product A: 20 in Alpha, 50 in Beta). Fixed ids keep
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

    public static string AlphaId => Warehouses[0].Id;

    // The Catalog seed products (CatalogInitialData's fixed ids) with the stock they used to carry, now
    // received into Alpha so the local storefront starts with the same numbers (ADR-0052 §8).
    public static IReadOnlyList<SeedReceipt> Receipts =>
    [
        new("b1000000-0000-0000-0000-000000000001", 50),
        new("b1000000-0000-0000-0000-000000000002", 30),
        new("b1000000-0000-0000-0000-000000000003", 45),
        new("b1000000-0000-0000-0000-000000000004", 15),
        new("b1000000-0000-0000-0000-000000000005", 25),
        new("b1000000-0000-0000-0000-000000000006", 35),
        new("b1000000-0000-0000-0000-000000000007", 40),
        new("b1000000-0000-0000-0000-000000000008", 20),
    ];
}
