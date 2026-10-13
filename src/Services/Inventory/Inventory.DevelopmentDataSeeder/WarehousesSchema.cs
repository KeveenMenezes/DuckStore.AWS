namespace Inventory.DevelopmentDataSeeder;

// Inventory has no Function project, so the seeder owns the table's names and keys. Mirrors
// infra/constructs/inventory-dynamodb.ts and the graphql/resolvers/inventory resolvers (ADR-0052 §2).
public static class WarehousesSchema
{
    public const string TableName = "inventory-warehouses";
    public const string PartitionKey = "WarehouseId";

    public const string WarehouseType = "Warehouse";
    public const string CodeGuardType = "CodeGuard";

    // The guard item that makes a warehouse code unique: written in the same transaction as the
    // warehouse, never deleted (codes are immutable, warehouses are never deleted).
    public static string CodeGuardKey(string code) => $"CODE#{code}";
}
