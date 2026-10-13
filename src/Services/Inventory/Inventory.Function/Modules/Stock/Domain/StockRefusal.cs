namespace Inventory.Function.Modules.Stock.Domain;

// An expected "no" to a movement, returned as data so the resolver can raise it with its errorType
// (ADR-0052 §7, the ADR-0050 §10 contract). Field is the GraphQL input field it points at.
public sealed record StockRefusal(string ErrorType, string Message, string? Field = null, int? Available = null)
{
    public static StockRefusal BadRequest(string field, string message) => new("BadRequest", message, field);

    public static StockRefusal WarehouseNotFound(string field) =>
        new("WarehouseNotFound", "Warehouse not found", field);

    public static StockRefusal WarehouseInactive(string field, string code) =>
        new("WarehouseInactive", $"Warehouse {code} is inactive", field);

    public static StockRefusal InsufficientStock(int available) =>
        new("InsufficientStock", $"Only {available} available", "quantity", available);

    public static StockRefusal IdempotencyKeyReused() =>
        new("IdempotencyKeyReused", "This idempotencyKey was already used for a different movement", "idempotencyKey");

    // Every optimistic retry lost to concurrent movements on the same balance; nothing was written.
    public static StockRefusal TransactionConflict() =>
        new("TransactionConflict", "The stock changed concurrently; retry");
}
