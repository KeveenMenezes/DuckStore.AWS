namespace Inventory.Function.Modules.Warehouses.Domain;

// What a stock movement needs to know about a warehouse (ADR-0052 §2): its id, its immutable code and
// whether it is active. Read fresh for every movement and re-checked inside the write transaction.
public sealed record WarehouseSnapshot(string Id, string Code, bool Active);
