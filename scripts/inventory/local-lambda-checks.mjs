// The stock movement Lambdas (ADR-0052 §7) invoked through the Aspire Lambda emulator, with the result
// checked in DynamoDB Local. Needs the AppHost running; the emulator URL is on the dashboard
// (LambdaServiceEmulator, http endpoint).
// Usage: node scripts/inventory/local-lambda-checks.mjs http://localhost:<emulator-port>
import { execFileSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'

const EMULATOR = process.argv[2]
if (!EMULATOR) throw new Error('usage: local-lambda-checks.mjs <emulator-url>')
const ALPHA = '0f6c3a52-6d0e-4b8e-9a51-3b1d2c4e5f01' // seeded, active
const BETA = '0f6c3a52-6d0e-4b8e-9a51-3b1d2c4e5f02' // seeded, active
const env = { ...process.env, AWS_ACCESS_KEY_ID: 'x', AWS_SECRET_ACCESS_KEY: 'x', AWS_REGION: 'us-east-1' }

async function invoke(fn, payload) {
  const res = await fetch(`${EMULATOR}/2015-03-31/functions/${fn}/invocations`, { method: 'POST', body: JSON.stringify(payload) })
  return res.json()
}
function dynamo(...args) {
  const out = execFileSync('aws', ['dynamodb', ...args, '--endpoint-url', 'http://localhost:8000', '--output', 'json'], { env }).toString()
  return out.trim() ? JSON.parse(out) : {}
}
function setActive(id, active) {
  dynamo('update-item', '--table-name', 'inventory-warehouses', '--key', JSON.stringify({ WarehouseId: { S: id } }),
    '--update-expression', 'SET Active = :a', '--expression-attribute-values', JSON.stringify({ ':a': { BOOL: active } }))
}
function balance(productId, warehouseId) {
  const item = dynamo('get-item', '--table-name', 'inventory-stock', '--key',
    JSON.stringify({ ProductId: { S: productId }, WarehouseId: { S: warehouseId } })).Item
  return item ? Number(item.Available.N) : null
}
function ledgerSum(productId, warehouseId) {
  const items = dynamo('query', '--table-name', 'inventory-movements', '--key-condition-expression', 'ProductId = :p',
    '--expression-attribute-values', JSON.stringify({ ':p': { S: productId } })).Items
  return items.filter((i) => i.WarehouseId.S === warehouseId).reduce((sum, i) => sum + Number(i.Quantity.N), 0)
}

const results = []
function check(label, actual, ok) {
  results.push(ok)
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${ok ? '' : `  -> ${JSON.stringify(actual)}`}`)
}
const refusal = (r) => r?.Refusal ?? r?.refusal
const movements = (r) => r?.Movements ?? r?.movements ?? []
const product = `local-${randomUUID()}`
const receipt = (key, warehouseId, quantity, invoice = 'NF-1') => invoke('inventory-record-stock-receipt',
  { IdempotencyKey: key, ProductId: product, WarehouseId: warehouseId, Quantity: quantity, InvoiceNumber: invoice, Supplier: 'ACME', Note: null, Actor: 'local-dev' })
const adjust = (key, warehouseId, direction, quantity, reason = 'Damage', note = null) => invoke('inventory-record-stock-adjustment',
  { IdempotencyKey: key, ProductId: product, WarehouseId: warehouseId, Direction: direction, Quantity: quantity, Reason: reason, Note: note, Actor: 'local-dev' })
const transfer = (key, from, to, quantity) => invoke('inventory-transfer-stock',
  { IdempotencyKey: key, ProductId: product, FromWarehouseId: from, ToWarehouseId: to, Quantity: quantity, Note: null, Actor: 'local-dev' })

try {
  const r1Key = randomUUID()
  const r1 = await receipt(r1Key, ALPHA, 20)
  check('receipt of 20 into ALPHA', r1, movements(r1).length === 1 && movements(r1)[0].BalanceAfter === 20 && balance(product, ALPHA) === 20)
  check('no invoice -> BadRequest invoiceNumber', null, refusal(await receipt(randomUUID(), ALPHA, 1, ' '))?.Field === 'invoiceNumber')
  await receipt(randomUUID(), BETA, 50)
  check('receipt of 50 into BETA', null, balance(product, BETA) === 50)

  const a1 = await adjust(randomUUID(), ALPHA, 'Decrease', 2)
  check('adjust -2 (Damage) on ALPHA -> 18', a1, movements(a1)[0]?.Quantity === -2 && balance(product, ALPHA) === 18)
  const tooMuch = await adjust(randomUUID(), ALPHA, 'Decrease', 30)
  check('adjust -30 -> InsufficientStock, available 18', tooMuch, refusal(tooMuch)?.ErrorType === 'InsufficientStock' && refusal(tooMuch)?.Available === 18)
  check('Other without a note -> BadRequest note', null, refusal(await adjust(randomUUID(), ALPHA, 'Increase', 1, 'Other'))?.Field === 'note')

  const tKey = randomUUID()
  const t1 = await transfer(tKey, BETA, ALPHA, 10)
  check('transfer 10 BETA -> ALPHA: 40 / 28, two linked rows', t1,
    movements(t1).length === 2 && balance(product, BETA) === 40 && balance(product, ALPHA) === 28
    && movements(t1).every((m) => m.TransferId === tKey))
  check('transfer more than the origin -> InsufficientStock', null, refusal(await transfer(randomUUID(), BETA, ALPHA, 999))?.ErrorType === 'InsufficientStock')
  check('transfer to itself -> BadRequest toWarehouseId', null, refusal(await transfer(randomUUID(), ALPHA, ALPHA, 1))?.Field === 'toWarehouseId')

  setActive(BETA, false)
  check('receipt into inactive BETA -> WarehouseInactive', null, refusal(await receipt(randomUUID(), BETA, 1))?.ErrorType === 'WarehouseInactive')
  check('transfer into inactive BETA -> WarehouseInactive', null, refusal(await transfer(randomUUID(), ALPHA, BETA, 1))?.ErrorType === 'WarehouseInactive')
  check('decrease on inactive BETA is allowed', null, !refusal(await adjust(randomUUID(), BETA, 'Decrease', 5, 'Count')))
  check('transfer out of inactive BETA is allowed', null, !refusal(await transfer(randomUUID(), BETA, ALPHA, 5)))
  setActive(BETA, true)

  check('unknown warehouse -> WarehouseNotFound', null, refusal(await receipt(randomUUID(), 'nope', 1))?.ErrorType === 'WarehouseNotFound')
  const replay = await receipt(r1Key, ALPHA, 20)
  check('replay of the first receipt returns it, no second write', replay,
    movements(replay)[0]?.Id === r1Key && balance(product, ALPHA) === 33)
  check('same key, different request -> IdempotencyKeyReused', null, refusal(await receipt(r1Key, ALPHA, 21))?.ErrorType === 'IdempotencyKeyReused')
  const dupKey = randomUUID()
  const [d1, d2] = await Promise.all([receipt(dupKey, ALPHA, 3), receipt(dupKey, ALPHA, 3)])
  check('two concurrent sends of one key record it once', [d1, d2],
    movements(d1)[0]?.Id === dupKey && movements(d2)[0]?.Id === dupKey && balance(product, ALPHA) === 36)
  const concurrent = await Promise.all([1, 2, 3, 4, 5].map(() => receipt(randomUUID(), ALPHA, 1)))
  const ok = concurrent.filter((r) => !refusal(r)).length
  check(`five concurrent receipts of 1 (different keys): ${ok} recorded, balance matches`, concurrent, balance(product, ALPHA) === 36 + ok)
  check('ledger adds up to the balance (ALPHA)', null, ledgerSum(product, ALPHA) === balance(product, ALPHA))
  check('ledger adds up to the balance (BETA)', null, ledgerSum(product, BETA) === balance(product, BETA))
} finally {
  setActive(BETA, true)
}

console.log(`\n${results.filter(Boolean).length}/${results.length} passed`)
process.exit(results.every(Boolean) ? 0 : 1)
