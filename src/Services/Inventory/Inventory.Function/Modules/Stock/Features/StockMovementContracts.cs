namespace Inventory.Function.Modules.Stock.Features;

// The Lambda boundary shared by the three movement functions (ADR-0052 §7): the movements recorded or
// replayed, or a refusal as data. Every type here is registered in InventorySerializerContext.
public sealed record StockMovementResponse(List<StockMovementDto>? Movements, StockRefusalDto? Refusal)
{
    public static StockMovementResponse From(MovementResult result) => new(
        result.Refusal is null ? [.. result.Movements.Select(StockMovementDto.From)] : null,
        result.Refusal is { } refusal ? StockRefusalDto.From(refusal) : null);

    public static StockMovementResponse Refused(StockRefusal refusal) => new(null, StockRefusalDto.From(refusal));
}

public sealed record StockMovementDto(
    string Id, string ProductId, string Type, int Quantity, int BalanceAfter, string WarehouseId, string WarehouseCode,
    string? InvoiceNumber, string? Supplier, string? Reason, string? Note, string? TransferId,
    string? CounterpartWarehouseId, string CreatedAt, string CreatedBy)
{
    public static StockMovementDto From(StockMovement movement) => new(
        movement.MovementId, movement.ProductId, movement.Type.ToString(), movement.Quantity, movement.BalanceAfter,
        movement.WarehouseId, movement.WarehouseCode, movement.InvoiceNumber, movement.Supplier,
        movement.Reason?.ToString(), movement.Note, movement.TransferId, movement.CounterpartWarehouseId,
        movement.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), movement.CreatedBy);
}

public sealed record StockRefusalDto(string ErrorType, string Message, string? Field, int? Available)
{
    public static StockRefusalDto From(StockRefusal refusal) =>
        new(refusal.ErrorType, refusal.Message, refusal.Field, refusal.Available);
}
