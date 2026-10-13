// Runs graphql/resolvers/inventory against the real APPSYNC_JS runtime with `aws appsync evaluate-code`
// (no deploy; needs AWS credentials). Cases mirror lib/warehouse-validation.test.ts (ADR-0052 §4).
// Usage: node scripts/inventory/evaluate-resolvers.mjs "$PWD"
import { execFileSync } from 'node:child_process'
import { writeFileSync, mkdtempSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

const root = process.argv[2]
const dir = mkdtempSync(join(tmpdir(), 'inv-eval-'))
const R = (kind, name) => join(root, 'graphql/resolvers/inventory', kind, name)
const CREATE = R('mutations', 'Mutation.createWarehouse.js')
const UPDATE = R('mutations', 'Mutation.updateWarehouse.js')
const ACTIVE = R('mutations', 'Mutation.setWarehouseActive.js')
const LIST = R('queries', 'Query.warehouses.js')
const GET = R('queries', 'Query.warehouse.js')

let n = 0
function evaluate(code, fn, context) {
  const file = join(dir, `ctx-${n++}.json`)
  writeFileSync(file, JSON.stringify(context))
  const out = execFileSync('aws', ['appsync', 'evaluate-code', '--runtime', 'name=APPSYNC_JS,runtimeVersion=1.0.0',
    '--code', `file://${code}`, '--context', `file://${file}`, '--function', fn, '--output', 'json'])
  const json = JSON.parse(out.toString())
  if (json.error?.codeErrors?.length) throw new Error(JSON.stringify(json.error))
  return {
    error: json.error?.message,
    result: json.evaluationResult ? JSON.parse(json.evaluationResult) : undefined,
    outErrors: json.outErrors ? JSON.parse(json.outErrors) : [],
  }
}

const results = []
function check(label, actual, ok) {
  results.push({ label, ok })
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${ok ? '' : `  -> ${JSON.stringify(actual)}`}`)
}

const admin = { sub: 'admin-sub', groups: ['Admin'] }
const keeper = { sub: 'keeper-sub', groups: ['StockKeeper'] }
const validAddress = { street: 'Avenida Paulista', number: '1000', complement: null, district: 'Bela Vista', city: 'São Paulo', state: 'SP', zipCode: '01310100' }
const validFields = { name: 'Polo Alpha', address: validAddress, priority: 1 }
const createInput = (patch = {}) => ({ code: 'GAMMA', ...validFields, ...patch })
const withAddress = (patch) => ({ address: { ...validAddress, ...patch } })

// 1. Group gate on every resolver
const identities = { Seller: { sub: 's', groups: ['Seller'] }, Customer: { sub: 'c', groups: ['Customer'] }, 'no group': { sub: 'x' } }
const gated = [
  ['createWarehouse', CREATE, { input: createInput() }],
  ['updateWarehouse', UPDATE, { input: { id: 'w1', ...validFields } }],
  ['setWarehouseActive', ACTIVE, { id: 'w1', active: false }],
  ['warehouses', LIST, {}],
  ['warehouse', GET, { id: 'w1' }],
]
for (const [field, code, args] of gated) {
  for (const [who, identity] of Object.entries(identities)) {
    const r = evaluate(code, 'request', { identity, arguments: args })
    check(`${field}: ${who} -> Unauthorized`, r, r.error === 'Unauthorized')
  }
  for (const identity of [admin, keeper]) {
    const r = evaluate(code, 'request', { identity, arguments: args })
    check(`${field}: ${identity.groups[0]} allowed`, r, !r.error)
  }
}

// 2. Validation — the same cases as lib/warehouse-validation.test.ts
const fieldCases = [
  ['empty name', { name: '   ' }, 'name must'],
  ['name over 80', { name: 'x'.repeat(81) }, 'name must'],
  ['empty street', withAddress({ street: '' }), 'street must'],
  ['street over 120', withAddress({ street: 'x'.repeat(121) }), 'street must'],
  ['number over 10', withAddress({ number: '12345678901' }), 'number must'],
  ['complement over 60', withAddress({ complement: 'x'.repeat(61) }), 'complement must'],
  ['empty district', withAddress({ district: '' }), 'district must'],
  ['empty city', withAddress({ city: '' }), 'city must'],
  ['unknown UF', withAddress({ state: 'XX' }), 'state must'],
  ['zipCode with 7 digits', withAddress({ zipCode: '0131010' }), 'zipCode must'],
  ['zipCode with letters', withAddress({ zipCode: '0131O100' }), 'zipCode must'],
  ['priority 0', { priority: 0 }, 'priority must'],
  ['priority 1000', { priority: 1000 }, 'priority must'],
  ['priority 1.5', { priority: 1.5 }, 'priority must'],
]
for (const [label, patch, message] of fieldCases) {
  const c = evaluate(CREATE, 'request', { identity: admin, arguments: { input: createInput(patch) } })
  check(`createWarehouse: rejects ${label}`, c, c.error?.startsWith(message))
  const u = evaluate(UPDATE, 'request', { identity: admin, arguments: { input: { id: 'w1', ...validFields, ...patch } } })
  check(`updateWarehouse: rejects ${label}`, u, u.error?.startsWith(message))
}
for (const code of ['A', 'A'.repeat(21), '-ALPHA', 'AL PHA', 'ALPHA_1', '', 'ÁLPHA']) {
  const r = evaluate(CREATE, 'request', { identity: admin, arguments: { input: createInput({ code }) } })
  check(`createWarehouse: rejects code ${JSON.stringify(code)}`, r, r.error?.startsWith('code must'))
}
for (const [code, expected] of [['a1', 'A1'], ['  alpha ', 'ALPHA'], ['SP-01', 'SP-01'], ['A'.repeat(20), 'A'.repeat(20)]]) {
  const r = evaluate(CREATE, 'request', { identity: admin, arguments: { input: createInput({ code }) } })
  check(`createWarehouse: accepts code ${JSON.stringify(code)}`, r,
    r.result?.transactItems?.[1]?.key?.WarehouseId?.S === `CODE#${expected}`)
}
for (const [label, patch] of [['priority 999', { priority: 999 }], ['name of 80', { name: 'x'.repeat(80) }], ['complement of 60', withAddress({ complement: 'x'.repeat(60) })]]) {
  const r = evaluate(CREATE, 'request', { identity: admin, arguments: { input: createInput(patch) } })
  check(`createWarehouse: accepts ${label}`, r, !r.error)
}

// 3. createWarehouse request shape and normalization
{
  const r = evaluate(CREATE, 'request', { identity: keeper, arguments: { input: createInput({ code: ' gamma ', name: '  Polo Gamma ', ...withAddress({ state: ' pe ', zipCode: '50000-000', complement: '  ' }) }) } })
  const [warehouse, guard] = r.result.transactItems
  const a = warehouse.attributeValues
  check('createWarehouse: two PutItems on inventory-warehouses, both attribute_not_exists', r,
    r.result.operation === 'TransactWriteItems' && warehouse.table === 'inventory-warehouses' && guard.table === 'inventory-warehouses'
    && warehouse.condition.expression === 'attribute_not_exists(WarehouseId)' && guard.condition.expression === 'attribute_not_exists(WarehouseId)')
  check('createWarehouse: normalized (code, name, UF, CEP, empty complement omitted, Active, UpdatedBy)', a,
    a.Code.S === 'GAMMA' && a.Name.S === 'Polo Gamma' && a.State.S === 'PE' && a.ZipCode.S === '50000000'
    && a.Complement === undefined && a.Active.BOOL === true && a.UpdatedBy.S === 'keeper-sub' && a.Type.S === 'Warehouse')
  check('createWarehouse: guard points at the warehouse id', guard,
    guard.key.WarehouseId.S === 'CODE#GAMMA' && guard.attributeValues.Type.S === 'CodeGuard'
    && guard.attributeValues.TargetWarehouseId.S === warehouse.key.WarehouseId.S)
}

// 4. createWarehouse response
const stashItem = { WarehouseId: 'w1', Type: 'Warehouse', Code: 'ALPHA', Name: 'Polo Alpha', Street: 'Av', Number: '1', District: 'D', City: 'C', State: 'SP', ZipCode: '01310100', Priority: 1, Active: true, CreatedAt: 't', UpdatedAt: 't' }
const cancelled = (reasons) => ({ identity: admin, arguments: {}, stash: { item: stashItem },
  error: { type: 'DynamoDB:TransactionCanceledException', message: 'Transaction cancelled' },
  result: { cancellationReasons: reasons.map((type) => ({ type, message: null })), keys: [] } })
{
  const r = evaluate(CREATE, 'response', cancelled(['None', 'ConditionalCheckFailed']))
  check('createWarehouse: guard condition failed -> WarehouseCodeTaken', r, r.error === 'Warehouse code ALPHA is already taken')
  const c = evaluate(CREATE, 'response', cancelled(['None', 'TransactionConflict']))
  check('createWarehouse: TransactionConflict -> retryable error', c, c.error?.startsWith('Another write'))
  const o = evaluate(CREATE, 'response', { identity: admin, arguments: {}, stash: { item: stashItem }, error: { type: 'DynamoDB:ProvisionedThroughputExceededException', message: 'boom' } })
  check('createWarehouse: other error passed through', o, o.error === 'boom')
  const ok = evaluate(CREATE, 'response', { identity: admin, arguments: {}, stash: { item: stashItem }, result: { keys: [] } })
  check('createWarehouse: success maps the stashed item', ok, ok.result?.code === 'ALPHA' && ok.result?.address?.zipCode === '01310100' && ok.result?.address?.complement === null)
}

// 5. update / setActive
{
  const r = evaluate(UPDATE, 'request', { identity: keeper, arguments: { input: { id: 'w1', ...validFields, name: 'Novo nome', ...withAddress({ complement: 'Galpão 2' }) } } })
  check('updateWarehouse: UpdateItem conditioned on attribute_exists AND Type, no Code in the update', r,
    r.result.operation === 'UpdateItem' && r.result.condition.expression === 'attribute_exists(WarehouseId) AND #type = :warehouse'
    && !/(^|[ ,])Code =/.test(r.result.update.expression) && r.result.update.expression.includes('Complement = :complement'))
  const noComplement = evaluate(UPDATE, 'request', { identity: keeper, arguments: { input: { id: 'w1', ...validFields } } })
  check('updateWarehouse: empty complement is removed', noComplement, noComplement.result.update.expression.endsWith('REMOVE Complement'))
  const a = evaluate(ACTIVE, 'request', { identity: keeper, arguments: { id: 'w1', active: false } })
  check('setWarehouseActive: sets Active, UpdatedAt, UpdatedBy and bumps Version, with the same condition', a,
    a.result.update.expression === 'SET Active = :active, UpdatedAt = :now, UpdatedBy = :by ADD Version :one'
    && a.result.update.expressionValues[':one']?.N === 1
    && a.result.condition.expression === 'attribute_exists(WarehouseId) AND #type = :warehouse')
  for (const [field, code] of [['updateWarehouse', UPDATE], ['setWarehouseActive', ACTIVE]]) {
    const nf = evaluate(code, 'response', { identity: admin, arguments: {}, error: { type: 'DynamoDB:ConditionalCheckFailedException', message: 'cond' } })
    check(`${field}: ConditionalCheckFailed -> NotFound`, nf, nf.error === 'Warehouse not found')
    const ok = evaluate(code, 'response', { identity: admin, arguments: {}, result: { ...stashItem, Active: false } })
    check(`${field}: maps ALL_NEW`, ok, ok.result?.id === 'w1' && ok.result?.active === false)
  }
}

// 6. queries
{
  const items = [
    { ...stashItem, WarehouseId: 'b', Code: 'BETA', Priority: 2 },
    { ...stashItem, WarehouseId: 'k', Code: 'KILO', Priority: 10 },
    { WarehouseId: 'CODE#ALPHA', Type: 'CodeGuard', TargetWarehouseId: 'a' },
    { ...stashItem, WarehouseId: 'z', Code: 'ZULU', Priority: 1 },
    { ...stashItem, WarehouseId: 'a', Code: 'ALPHA', Priority: 1 },
  ]
  const req = evaluate(LIST, 'request', { identity: keeper, arguments: {} })
  check('warehouses: Scan filtered on Type = Warehouse', req, req.result.operation === 'Scan' && req.result.filter.expression === '#type = :warehouse')
  // The filter runs in DynamoDB; the response sees only warehouses.
  const r = evaluate(LIST, 'response', { identity: keeper, arguments: {}, result: { items: items.filter((i) => i.Type === 'Warehouse') } })
  check('warehouses: sorted by priority, then code', r, JSON.stringify(r.result?.map((w) => w.code)) === '["ALPHA","ZULU","BETA","KILO"]')
  const truncated = evaluate(LIST, 'response', { identity: keeper, arguments: {}, result: { items: [items[0]], nextToken: 'more' } })
  check('warehouses: a second Scan page is reported as Truncated, with the first page returned', truncated,
    truncated.result?.length === 1 && truncated.outErrors.some((e) => e.type === 'Truncated'))
  const whole = evaluate(LIST, 'response', { identity: keeper, arguments: {}, result: { items: [items[0]] } })
  check('warehouses: one page, no error', whole, whole.outErrors.length === 0)
  const g = evaluate(GET, 'response', { identity: keeper, arguments: { id: 'CODE#ALPHA' }, result: items.find((i) => i.Type === 'CodeGuard') })
  check('warehouse: a guard reads as null', g, g.result === null)
  const missing = evaluate(GET, 'response', { identity: keeper, arguments: { id: 'nope' }, result: null })
  check('warehouse: unknown id reads as null', missing, missing.result === null)
  const found = evaluate(GET, 'response', { identity: keeper, arguments: { id: 'b' }, result: items[0] })
  check('warehouse: maps the item', found, found.result?.code === 'BETA')
}

// 7. stock (ADR-0052 §7)
const S = (kind, name) => R(kind, name)
const sub = { sub: 'keeper-sub', groups: ['StockKeeper'] }
const receiptInput = { idempotencyKey: 'k1', productId: 'p1', warehouseId: 'w1', quantity: 5, invoiceNumber: 'NF-1' }
const stockGated = [
  ['recordStockReceipt', S('mutations', 'Mutation.recordStockReceipt.js'), { input: receiptInput }],
  ['recordStockAdjustment', S('mutations', 'Mutation.recordStockAdjustment.js'), { input: { ...receiptInput, direction: 'Decrease', reason: 'Count' } }],
  ['transferStock', S('mutations', 'Mutation.transferStock.js'), { input: { idempotencyKey: 'k1', productId: 'p1', fromWarehouseId: 'w1', toWarehouseId: 'w2', quantity: 1 } }],
  ['stockByProduct', S('queries', 'Query.stockByProduct.js'), { productId: 'p1' }],
  ['stockMovementsByProduct', S('queries', 'Query.stockMovementsByProduct.js'), { productId: 'p1' }],
  ['stockByWarehouse', S('queries', 'Query.stockByWarehouse.js'), { warehouseId: 'w1' }],
  ['stockMovementsByWarehouse', S('queries', 'Query.stockMovementsByWarehouse.js'), { warehouseId: 'w1' }],
]
for (const [field, code, args] of stockGated) {
  for (const [who, identity] of Object.entries(identities)) {
    const r = evaluate(code, 'request', { identity, arguments: args })
    check(`${field}: ${who} -> Unauthorized`, r, r.error === 'Unauthorized')
  }
  const ok = evaluate(code, 'request', { identity: admin, arguments: args })
  check(`${field}: Admin allowed`, ok, !ok.error)
}
{
  const CHECK = S('mutations', 'Mutation.stock.checkProduct.js')
  const receipt = evaluate(CHECK, 'request', { identity: sub, arguments: { input: receiptInput } })
  check('checkProduct: GetItem on the product id', receipt, receipt.result?.operation === 'GetItem' && receipt.result?.key?.Id?.S === 'p1')
  const decrease = evaluate(CHECK, 'request', { identity: sub, arguments: { input: { ...receiptInput, direction: 'Decrease' } } })
  check('checkProduct: skipped for a decrease', decrease, !decrease.error && decrease.result?.operation === undefined)
  const missing = evaluate(CHECK, 'response', { identity: sub, arguments: { input: receiptInput }, result: null })
  check('checkProduct: missing product -> ProductNotFound', missing, missing.error === 'Product not found')
  const found = evaluate(CHECK, 'response', { identity: sub, arguments: { input: receiptInput }, result: { Id: 'p1' } })
  check('checkProduct: existing product passes', found, !found.error)

  const RECORD = S('mutations', 'Mutation.recordStockReceipt.record.js')
  const invoke = evaluate(RECORD, 'request', { identity: sub, arguments: { input: { ...receiptInput, createdBy: 'forged' } } })
  check('receipt record: Invoke with Actor from the token, nothing else about the author', invoke,
    invoke.result?.operation === 'Invoke' && invoke.result.payload.Actor === 'keeper-sub' && !('CreatedBy' in invoke.result.payload)
    && invoke.result.payload.Supplier === null && invoke.result.payload.InvoiceNumber === 'NF-1')
  const refused = evaluate(RECORD, 'response', { identity: sub, arguments: {}, result: { Refusal: { ErrorType: 'InsufficientStock', Message: 'Only 3 available', Field: 'quantity', Available: 3 } } })
  check('record: a refusal becomes a typed error', refused, refused.error === 'Only 3 available')
  const thrown = evaluate(RECORD, 'response', { identity: sub, arguments: {}, error: { type: 'Lambda:Unhandled', message: 'NullReferenceException at ...' } })
  check('record: an unhandled Lambda error is InternalError with no detail', thrown, thrown.error === 'The stock movement could not be recorded')
  const ok = evaluate(RECORD, 'response', { identity: sub, arguments: {}, result: { Movements: [{ Id: 'k1', ProductId: 'p1', Type: 'Receipt', Quantity: 5, BalanceAfter: 5, WarehouseId: 'w1', WarehouseCode: 'ALPHA', InvoiceNumber: 'NF-1', Supplier: null, Reason: null, Note: null, TransferId: null, CounterpartWarehouseId: null, CreatedAt: '2026-10-13T12:00:00.000Z', CreatedBy: 'keeper-sub' }] } })
  check('record: movements mapped to the GraphQL shape', ok, ok.result?.movements?.[0]?.id === 'k1' && ok.result.movements[0].balanceAfter === 5 && ok.result.movements[0].createdBy === 'keeper-sub')

  const ADJ = S('mutations', 'Mutation.recordStockAdjustment.record.js')
  const adj = evaluate(ADJ, 'request', { identity: sub, arguments: { input: { ...receiptInput, direction: 'Decrease', reason: 'Other', note: 'x' } } })
  check('adjustment record: direction and reason passed as enum names', adj, adj.result?.payload?.Direction === 'Decrease' && adj.result.payload.Reason === 'Other')
  const TR = S('mutations', 'Mutation.transferStock.js')
  const tr = evaluate(TR, 'request', { identity: sub, arguments: { input: { idempotencyKey: 'k1', productId: 'p1', fromWarehouseId: 'w1', toWarehouseId: 'w2', quantity: 1 } } })
  check('transfer: Invoke with both warehouses and the Actor', tr, tr.result?.payload?.FromWarehouseId === 'w1' && tr.result.payload.ToWarehouseId === 'w2' && tr.result.payload.Actor === 'keeper-sub')
}
{
  const BYP = S('queries', 'Query.stockByProduct.js')
  const q = evaluate(BYP, 'response', { identity: sub, arguments: { productId: 'p1' }, result: { items: [
    { ProductId: 'p1', WarehouseId: 'w1', WarehouseCode: 'ALPHA', Available: 20, Reserved: 0 },
    { ProductId: 'p1', WarehouseId: 'w2', WarehouseCode: 'BETA', Available: 50 }] } })
  check('stockByProduct: total over every warehouse', q, q.result?.totalAvailable === 70 && q.result.warehouses.length === 2 && q.result.warehouses[1].reserved === 0)
  const MBP = S('queries', 'Query.stockMovementsByProduct.js')
  const req = evaluate(MBP, 'request', { identity: sub, arguments: { productId: 'p1', pageSize: 5, nextToken: 't' } })
  check('stockMovementsByProduct: LSI1, newest first, paged', req, req.result?.index === 'LSI1' && req.result.scanIndexForward === false && req.result.limit === 5 && req.result.nextToken === 't')
  for (const size of [0, 101, 1.5]) {
    const bad = evaluate(MBP, 'request', { identity: sub, arguments: { productId: 'p1', pageSize: size } })
    check(`stockMovementsByProduct: pageSize ${size} -> BadRequest`, bad, bad.error?.startsWith('pageSize must'))
    const badW = evaluate(S('queries', 'Query.stockByWarehouse.js'), 'request', { identity: sub, arguments: { warehouseId: 'w1', pageSize: size } })
    check(`stockByWarehouse: pageSize ${size} -> BadRequest`, badW, badW.error?.startsWith('pageSize must'))
  }
  const MBW = S('queries', 'Query.stockMovementsByWarehouse.movements.js')
  const w = evaluate(MBW, 'request', { identity: sub, arguments: { warehouseId: 'w1' } })
  check('stockMovementsByWarehouse: GSI1 by warehouse, newest first, default 20', w, w.result?.index === 'GSI1' && w.result.scanIndexForward === false && w.result.limit === 20)

  const NAMES = S('queries', 'Query.stock.productNames.js')
  const page = { items: [{ productId: 'p1', available: 1 }, { productId: 'p2', available: 2 }, { productId: 'p1', available: 3 }], nextToken: 'n' }
  const batch = evaluate(NAMES, 'request', { identity: sub, arguments: {}, prev: { result: page } })
  check('productNames: BatchGetItem with de-duplicated keys', batch,
    batch.result?.operation === 'BatchGetItem' && batch.result.tables.products.keys.length === 2)
  const empty = evaluate(NAMES, 'request', { identity: sub, arguments: {}, prev: { result: { items: [], nextToken: null } } })
  check('productNames: an empty page skips the call', empty, !empty.error && empty.result?.operation === undefined)
  const named = evaluate(NAMES, 'response', { identity: sub, arguments: {}, prev: { result: page }, result: { data: { products: [{ Id: 'p1', Name: 'Pato' }, null] } } })
  check('productNames: names mapped, a missing product is null, nextToken kept', named,
    named.result?.items?.[0]?.productName === 'Pato' && named.result.items[1].productName === null && named.result.items[2].productName === 'Pato' && named.result.nextToken === 'n')
}

// 3. The storefront's stock: read from CatalogView, never written by the product mutations (ADR-0052 §8)
{
  const P = (name) => join(root, 'graphql/resolvers/products/queries', name)
  const shopper = { sub: 'c', groups: ['Customer'] }
  const product = (item) => evaluate(P('Query.product.js'), 'response', { identity: shopper, arguments: { id: 'p1' }, result: item })
  const stockOnly = product({ Id: 'p1', Stock: 5, StockVersion: 3 })
  check('product: a stock-only item is not a product yet', stockOnly, !stockOnly.error && stockOnly.result === null)
  const noStock = product({ Id: 'p1', Name: 'Pato', Description: 'd' })
  check('product: no total yet reads as stock 0', noStock, noStock.result?.stock === 0)
  const listed = evaluate(P('Query.products.js'), 'response', { identity: shopper, arguments: {}, result: { items: [{ Id: 'p1', Name: 'Pato', Description: 'd' }] } })
  check('products: no total yet reads as stock 0', listed, listed.result?.items?.[0]?.stock === 0)

  // The product writes carry no stock: Inventory owns it.
  const W = (name) => join(root, 'graphql/resolvers/products/mutations', name)
  const seller = { sub: 's', groups: ['Seller'] }
  const input = { name: 'Pato', description: 'd', images: [{ imageId: '01HZXW5N8T2J3K4M5P6Q7R8S9A', isMain: true, order: 0 }], categoryIds: ['c1'] }
  const created = evaluate(W('Mutation.createProduct.js'), 'request', { identity: seller, arguments: { input } })
  check('createProduct: PutItem without Stock', created, created.result?.operation === 'PutItem' && !('Stock' in created.result.attributeValues))
  const updated = evaluate(W('Mutation.updateProduct.js'), 'request', { identity: seller, arguments: { input: { id: 'p1', ...input } } })
  check('updateProduct: no Stock in the update', updated, !updated.result?.update?.expression.includes('Stock'))
  const saga = evaluate(W('Mutation.createProductWithPrice.js'), 'request', { identity: seller, arguments: { input: { ...input, price: 10, cost: 4 } } })
  const sagaInput = JSON.parse(JSON.parse(saga.result?.params?.body ?? '{}').input ?? '{}')
  check('createProductWithPrice: saga input without stock', sagaInput, !('stock' in sagaInput) && sagaInput.price === '10')
}

const failed = results.filter((r) => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} passed`)
process.exit(failed.length ? 1 : 0)
