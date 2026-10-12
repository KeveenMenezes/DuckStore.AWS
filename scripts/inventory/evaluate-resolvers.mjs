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
  check('setWarehouseActive: sets Active, UpdatedAt, UpdatedBy with the same condition', a,
    a.result.update.expression === 'SET Active = :active, UpdatedAt = :now, UpdatedBy = :by'
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

const failed = results.filter((r) => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} passed`)
process.exit(failed.length ? 1 : 0)
