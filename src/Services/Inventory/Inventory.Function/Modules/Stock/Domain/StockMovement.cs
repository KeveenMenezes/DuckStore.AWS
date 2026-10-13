namespace Inventory.Function.Modules.Stock.Domain;

// One immutable ledger row: one change of one balance (ADR-0052 §7). Quantity is signed and
// BalanceAfter is the stock on hand right after it, so a warehouse's rows are a running sum.
public sealed record StockMovement(
    string ProductId,
    string MovementId,
    string WarehouseId,
    string WarehouseCode,
    StockMovementType Type,
    int Quantity,
    int BalanceAfter,
    string? InvoiceNumber,
    string? Supplier,
    StockAdjustmentReason? Reason,
    string? Note,
    string? TransferId,
    string? CounterpartWarehouseId,
    string PayloadHash,
    DateTime CreatedAt,
    string CreatedBy);
