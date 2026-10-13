namespace Inventory.Function.Modules.Warehouses.Data;

// Mirrors infra/constructs/inventory-dynamodb.ts and the graphql/resolvers/inventory resolvers
// (ADR-0052 §2).
public static class WarehousesSchema
{
    public const string TableName = "inventory-warehouses";
    public const string PartitionKey = "WarehouseId";

    public const string TypeAttribute = "Type";
    public const string CodeAttribute = "Code";
    public const string ActiveAttribute = "Active";

    // Counts the activation changes, written by setWarehouseActive (ADR-0052 §8).
    public const string VersionAttribute = "Version";

    public const string WarehouseType = "Warehouse";
    public const string CodeGuardType = "CodeGuard";

    // The guard item that makes a warehouse code unique: written in the same transaction as the
    // warehouse, never deleted (codes are immutable, warehouses are never deleted).
    public static string CodeGuardKey(string code) => $"CODE#{code}";
}
