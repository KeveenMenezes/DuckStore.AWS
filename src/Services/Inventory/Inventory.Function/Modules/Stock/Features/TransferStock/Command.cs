namespace Inventory.Function.Modules.Stock.Features.TransferStock;

public sealed record TransferStockCommand(
    string IdempotencyKey, string ProductId, string FromWarehouseId, string ToWarehouseId, int Quantity,
    string? Note, string Actor) : ICommand<MovementResult>
{
    public string Fingerprint() =>
        RequestFingerprint.Of("Transfer", ProductId, FromWarehouseId, ToWarehouseId, Quantity, Note);

    // Both rows of one transfer share the key (ADR-0052 §7).
    public static IReadOnlyList<string> MovementIdsOf(Guid key) => [$"{key}#OUT", $"{key}#IN"];
}
