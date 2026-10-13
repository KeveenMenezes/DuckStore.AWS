namespace Inventory.Function.Modules.Stock.Data;

// inventory-stock: one balance per product × warehouse (ADR-0052 §7). Mirrors
// infra/constructs/inventory-dynamodb.ts.
public static class StockSchema
{
    public const string TableName = "inventory-stock";
    public const string PartitionKey = "ProductId";
    public const string SortKey = "WarehouseId";

    // GSI1: a warehouse's balances, for the warehouse stock view.
    public const string ByWarehouseIndex = "GSI1";

    public const string AvailableAttribute = "Available";
    public const string ReservedAttribute = "Reserved";
    public const string WarehouseCodeAttribute = "WarehouseCode";
    public const string UpdatedAtAttribute = "UpdatedAt";
}
