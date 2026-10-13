namespace Inventory.Function.Modules.Stock.Features.RecordReceipt;

public sealed record RecordReceiptCommand(
    string IdempotencyKey, string ProductId, string WarehouseId, int Quantity,
    string? InvoiceNumber, string? Supplier, string? Note, string Actor) : ICommand<MovementResult>
{
    public string Fingerprint() =>
        RequestFingerprint.Of(nameof(StockMovementType.Receipt), ProductId, WarehouseId, Quantity, InvoiceNumber, Supplier, Note);
}
