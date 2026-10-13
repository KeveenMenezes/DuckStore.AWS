namespace Inventory.Function.Modules.Stock.Features.RecordAdjustment;

public sealed record RecordAdjustmentCommand(
    string IdempotencyKey, string ProductId, string WarehouseId, StockAdjustmentDirection Direction, int Quantity,
    StockAdjustmentReason Reason, string? Note, string Actor) : ICommand<MovementResult>
{
    public string Fingerprint() =>
        RequestFingerprint.Of(nameof(StockMovementType.Adjustment), ProductId, WarehouseId, Direction.ToString(), Quantity, Reason.ToString(), Note);
}
