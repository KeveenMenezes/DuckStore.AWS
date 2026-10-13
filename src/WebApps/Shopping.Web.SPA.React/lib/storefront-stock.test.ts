import { test } from 'node:test'
import assert from 'node:assert/strict'
import { storefrontTotal, type BalanceRow, type WarehouseState } from './storefront-stock.ts'

// The same cases as StorefrontStockTests in Inventory.UnitTests (ADR-0052 §8).
const warehouses = new Map<string, WarehouseState>([
  ['alpha', { id: 'alpha', active: true, version: 2 }],
  ['beta', { id: 'beta', active: true, version: 0 }],
  ['closed', { id: 'closed', active: false, version: 1 }],
])

test('sums only active warehouses', () => {
  const total = storefrontTotal([
    { warehouseId: 'alpha', available: 20, version: 3 },
    { warehouseId: 'beta', available: 50, version: 1 },
    { warehouseId: 'closed', available: 7, version: 4 },
  ], warehouses)
  assert.equal(total.available, 70)
})

test('a warehouse that cannot be found counts as inactive', () => {
  assert.equal(storefrontTotal([{ warehouseId: 'gone', available: 5, version: 1 }], warehouses).available, 0)
})

test('Version sums the counters of the balances and their warehouses, inactive ones included', () => {
  const total = storefrontTotal([
    { warehouseId: 'alpha', available: 1, version: 3 },
    { warehouseId: 'closed', available: 1, version: 4 },
  ], warehouses)
  assert.equal(total.version, 3 + 2 + 4 + 1)
})

test('a newer state always has a greater Version', () => {
  const before = storefrontTotal([{ warehouseId: 'alpha', available: 20, version: 3 }], warehouses)
  const newer: Array<[string, BalanceRow[], Map<string, WarehouseState>]> = [
    ['a balance moves', [{ warehouseId: 'alpha', available: 25, version: 4 }], warehouses],
    ['a balance appears in another warehouse', [
      { warehouseId: 'alpha', available: 20, version: 3 },
      { warehouseId: 'beta', available: 5, version: 1 },
    ], warehouses],
    ['its warehouse is deactivated', [{ warehouseId: 'alpha', available: 20, version: 3 }],
      new Map(warehouses).set('alpha', { id: 'alpha', active: false, version: 3 })],
  ]
  for (const [change, balances, state] of newer) {
    assert.ok(storefrontTotal(balances, state).version > before.version, change)
  }
})

test('no balance at all is zero at version zero', () => {
  assert.deepEqual(storefrontTotal([], warehouses), { available: 0, version: 0 })
})
