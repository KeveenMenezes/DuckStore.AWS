namespace Inventory.Function.Modules.Warehouses.Data;

public interface IWarehouseReader
{
    // Null for an unknown id or a CODE# guard key.
    Task<WarehouseSnapshot?> GetAsync(string warehouseId, CancellationToken cancellationToken);
}
