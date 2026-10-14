namespace Inventory.Function.Modules.Stock.Data;

// The reads behind a storefront total (ADR-0052 §8), both consistent: a product's balance rows and the
// warehouses they sit in.
public interface IStorefrontStockReader
{
    Task<StorefrontTotal> TotalForAsync(string productId, CancellationToken cancellationToken);

    // Every product holding a balance in the warehouse (GSI1 of inventory-stock, all pages).
    Task<IReadOnlyList<string>> ProductsInWarehouseAsync(string warehouseId, CancellationToken cancellationToken);
}
