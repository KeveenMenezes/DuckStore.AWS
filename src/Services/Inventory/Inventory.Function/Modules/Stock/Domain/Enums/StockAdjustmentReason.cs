namespace Inventory.Function.Modules.Stock.Domain.Enums;

// Other needs a note (ADR-0052 §7).
public enum StockAdjustmentReason
{
    Count,
    Loss,
    Damage,
    Return,
    Other,
}
