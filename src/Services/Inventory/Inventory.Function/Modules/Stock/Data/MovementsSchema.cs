namespace Inventory.Function.Modules.Stock.Data;

// inventory-movements: the immutable ledger, one row per change of one balance (ADR-0052 §7).
// Mirrors infra/constructs/inventory-dynamodb.ts.
public static class MovementsSchema
{
    public const string TableName = "inventory-movements";
    public const string PartitionKey = "ProductId";
    public const string SortKey = "MovementId";

    // LSI1: a product's history by date. GSI1: a warehouse's history by date.
    public const string ByCreatedAtIndex = "LSI1";
    public const string ByWarehouseIndex = "GSI1";

    public const string WarehouseIdAttribute = "WarehouseId";
    public const string CreatedAtAttribute = "CreatedAt";
}
