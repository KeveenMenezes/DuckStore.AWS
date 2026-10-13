namespace Inventory.Function.Modules.Stock.Data;

// One movement's write: the balances it changed, their ledger rows, and the warehouse states it relied
// on, re-checked inside the same transaction (ADR-0052 §7).
public sealed record StockWrite(
    IReadOnlyList<StockBalance> Balances,
    IReadOnlyList<StockMovement> Movements,
    IReadOnlyList<WarehouseCondition> Warehouses);

public sealed record WarehouseCondition(string WarehouseId, bool MustBeActive);

public enum SaveOutcome
{
    Saved,
    // A balance changed since it was read: re-read and decide again.
    BalanceChanged,
    // A ledger row with this MovementId already exists: the request is a replay (or a reused key).
    MovementExists,
    // A warehouse was deactivated (or removed) since it was read: re-read and decide again.
    WarehouseChanged,
}

public interface IStockRepository
{
    // A consistent read; an empty balance when the product has never been in this warehouse.
    Task<StockBalance> LoadAsync(string productId, WarehouseSnapshot warehouse, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMovement>> FindMovementsAsync(
        string productId, IReadOnlyList<string> movementIds, CancellationToken cancellationToken);

    Task<SaveOutcome> SaveAsync(StockWrite write, CancellationToken cancellationToken);
}
