// Moves the storefront's stock on AWS dev from Catalog to Inventory (ADR-0052 §8, rollout step 3).
// Creates the active DEV-VITRINE warehouse (with its code guard, like the seeder) and, for every
// CatalogView product whose Stock > 0 and has no Inventory total yet, records a receipt of that
// quantity through inventory-record-stock-receipt (invoice DEV-SEED, actor dev-seed), so the ledger
// and every rule apply. Ids and idempotency keys are derived from the code and the product, so a
// re-run creates nothing new. It ends by checking that every product has a StockVersion or shows 0.
//
// Dev data only — the receipts are immutable ledger rows.
// Usage (AWS credentials of the dev account in the environment):
//   node scripts/inventory/seed-dev-showcase.mjs --dry-run
//   node scripts/inventory/seed-dev-showcase.mjs
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

const dryRun = process.argv.includes('--dry-run')
const CODE = 'DEV-VITRINE'
const INVOICE = 'DEV-SEED'
const ACTOR = 'dev-seed'
const dir = mkdtempSync(join(tmpdir(), 'dev-showcase-'))

const aws = (...args) => JSON.parse(execFileSync('aws', [...args, '--output', 'json']).toString() || 'null')
// A stable UUID (v4 layout) from a name, so ids and keys are the same on every run.
function uuidFrom(name) {
  const h = createHash('sha256').update(name).digest()
  h[6] = (h[6] & 0x0f) | 0x40
  h[8] = (h[8] & 0x3f) | 0x80
  const x = h.subarray(0, 16).toString('hex')
  return `${x.slice(0, 8)}-${x.slice(8, 12)}-${x.slice(12, 16)}-${x.slice(16, 20)}-${x.slice(20)}`
}

function scanProducts() {
  const items = []
  let start
  do {
    const page = aws('dynamodb', 'scan', '--table-name', 'catalogview-products',
      '--projection-expression', 'Id, #n, Stock, StockVersion', '--expression-attribute-names', '{"#n":"Name"}',
      ...(start ? ['--exclusive-start-key', JSON.stringify(start)] : []))
    items.push(...page.Items)
    start = page.LastEvaluatedKey
  } while (start)
  return items
    .filter((i) => i.Name?.S)
    .map((i) => ({ id: i.Id.S, name: i.Name.S, stock: Number(i.Stock?.N ?? 0), version: i.StockVersion ? Number(i.StockVersion.N) : null }))
    .sort((a, b) => a.name.localeCompare(b.name))
}

const show = (label, products) => {
  console.log(`\n${label}`)
  for (const p of products) console.log(`  ${String(p.stock).padStart(5)}  ${p.version === null ? 'Catalog  ' : `v${p.version}`.padEnd(9)}  ${p.name}  (${p.id})`)
}

const warehouseId = uuidFrom(`warehouse:${CODE}`)
const before = scanProducts()
show('Storefront before (Stock, source, product):', before)
const pending = before.filter((p) => p.stock > 0 && p.version === null)
console.log(`\nWarehouse ${CODE} = ${warehouseId}; ${pending.length} receipt(s) to record.`)
if (dryRun) {
  for (const p of pending) console.log(`  would receive ${p.stock} of ${p.name} (key ${uuidFrom(`receipt:${CODE}:${p.id}`)})`)
  process.exit(0)
}

// The warehouse and its guard, both only if absent — the same write as Mutation.createWarehouse.js.
const now = new Date().toISOString()
const s = (v) => ({ S: v })
const transact = join(dir, 'warehouse.json')
writeFileSync(transact, JSON.stringify([
  { Put: { TableName: 'inventory-warehouses', ConditionExpression: 'attribute_not_exists(WarehouseId)', Item: {
    WarehouseId: s(warehouseId), Type: s('Warehouse'), Code: s(CODE), Name: s('Vitrine (dev)'),
    Street: s('Avenida Paulista'), Number: s('1000'), District: s('Bela Vista'), City: s('São Paulo'), State: s('SP'),
    ZipCode: s('01310100'), Priority: { N: '1' }, Active: { BOOL: true }, CreatedAt: s(now), UpdatedAt: s(now), UpdatedBy: s(ACTOR),
  } } },
  { Put: { TableName: 'inventory-warehouses', ConditionExpression: 'attribute_not_exists(WarehouseId)', Item: {
    WarehouseId: s(`CODE#${CODE}`), Type: s('CodeGuard'), TargetWarehouseId: s(warehouseId),
  } } },
]))
try {
  aws('dynamodb', 'transact-write-items', '--transact-items', `file://${transact}`)
  console.log(`Created ${CODE}.`)
} catch (err) {
  const guard = aws('dynamodb', 'get-item', '--table-name', 'inventory-warehouses', '--key', JSON.stringify({ WarehouseId: s(`CODE#${CODE}`) }))
  if (guard?.Item?.TargetWarehouseId?.S !== warehouseId) throw err
  console.log(`${CODE} already exists.`)
}

let failed = 0
for (const p of pending) {
  const payload = join(dir, `receipt-${p.id}.json`)
  const out = join(dir, `out-${p.id}.json`)
  writeFileSync(payload, JSON.stringify({
    IdempotencyKey: uuidFrom(`receipt:${CODE}:${p.id}`), ProductId: p.id, WarehouseId: warehouseId,
    Quantity: p.stock, InvoiceNumber: INVOICE, Supplier: null, Note: null, Actor: ACTOR,
  }))
  const call = aws('lambda', 'invoke', '--function-name', 'inventory-record-stock-receipt',
    '--cli-binary-format', 'raw-in-base64-out', '--payload', `file://${payload}`, out)
  const body = JSON.parse(readFileSync(out, 'utf8'))
  if (call.FunctionError || body.Refusal) {
    failed++
    console.log(`  FAILED ${p.name}: ${JSON.stringify(body.Refusal ?? body)}`)
  } else {
    console.log(`  received ${p.stock} of ${p.name}`)
  }
}

// The totals reach CatalogView through the stream publisher and EventBridge: wait for them.
const deadline = Date.now() + 90_000
let after = scanProducts()
while (after.some((p) => p.version === null && p.stock > 0) && Date.now() < deadline) {
  await new Promise((resolve) => setTimeout(resolve, 5_000))
  after = scanProducts()
}
show('Storefront after:', after)
const left = after.filter((p) => p.version === null && p.stock > 0)
const changed = after.filter((p) => { const b = before.find((x) => x.id === p.id); return b && b.stock !== p.stock })
console.log(`\n${left.length} product(s) still on Catalog's number; ${changed.length} product(s) whose number changed; ${failed} failed receipt(s).`)
process.exit(left.length || failed || changed.length ? 1 : 0)
