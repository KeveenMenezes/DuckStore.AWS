// The storefront total (ADR-0052 §8), a copy of Inventory.Function's StorefrontStock for the local
// backend: Available summed over balances in active warehouses, and Version as the sum of the write
// counters of those balances and their warehouses, inactive ones included.
// storefront-stock.test.ts pins the same cases as StorefrontStockTests.

export type BalanceRow = { warehouseId: string; available: number; version: number }
export type WarehouseState = { id: string; active: boolean; version: number }

export function storefrontTotal(
  balances: BalanceRow[],
  warehouses: Map<string, WarehouseState>,
): { available: number; version: number } {
  let available = 0
  let version = 0

  for (const balance of balances) {
    version += balance.version
    const warehouse = warehouses.get(balance.warehouseId)
    if (!warehouse) continue
    version += warehouse.version
    if (warehouse.active) available += balance.available
  }

  return { available, version }
}
