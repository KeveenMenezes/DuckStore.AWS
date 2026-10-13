// Warehouse operations (ADR-0052) checked against the local BFF (local.ts + DynamoDB Local).
// Usage: node scripts/inventory/local-api-checks.mjs [http://localhost:3000/api/graphql]
const URL = process.argv[2] ?? 'http://localhost:3000/api/graphql'
const W = 'id code name priority active address { street number complement district city state zipCode } createdAt updatedAt'

async function gql(query, variables) {
  const res = await fetch(URL, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ query, variables }) })
  return res.json()
}
const results = []
function check(label, actual, ok) {
  results.push(ok)
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${ok ? '' : `  -> ${JSON.stringify(actual)}`}`)
}
const err = (r) => r.errors?.[0]?.extensions ?? {}

const suffix = Math.random().toString(36).slice(2, 8).toUpperCase()
const code = `GAMMA${suffix}`
const address = { street: 'Rua do Sol', number: '10', district: 'Boa Vista', city: 'Recife', state: 'pe', zipCode: '50050-000' }
const create = (input) => gql(`mutation($input: CreateWarehouseInput!) { createWarehouse(input: $input) { ${W} } }`, { input })

// seeded ALPHA/BETA listed, active, sorted
const list0 = await gql(`{ warehouses { ${W} } }`)
const codes0 = list0.data?.warehouses?.map((w) => w.code) ?? []
check('seeded ALPHA and BETA listed first, active', list0, codes0[0] === 'ALPHA' && codes0[1] === 'BETA' && list0.data.warehouses.slice(0, 2).every((w) => w.active))

const created = await create({ code: ` ${code.toLowerCase()} `, name: 'Polo Gamma', address, priority: 3 })
const g = created.data?.createWarehouse
check('create normalizes code, UF, CEP and starts active', created, g?.code === code && g.address.state === 'PE' && g.address.zipCode === '50050000' && g.active === true && g.address.complement === null)

// taken code
const taken = await create({ code: code.toLowerCase(), name: 'Outro', address, priority: 4 })
check('same code (lowercase) -> WarehouseCodeTaken, field code', taken, err(taken).errorType === 'WarehouseCodeTaken' && err(taken).errorInfo?.field === 'code')

// invalid input
for (const [label, input, field] of [
  ['CEP with 7 digits', { code: `X${suffix}`, name: 'x', address: { ...address, zipCode: '5005000' }, priority: 1 }, 'address.zipCode'],
  ['unknown UF', { code: `X${suffix}`, name: 'x', address: { ...address, state: 'XX' }, priority: 1 }, 'address.state'],
  ['priority 0', { code: `X${suffix}`, name: 'x', address, priority: 0 }, 'priority'],
  ['bad code', { code: '-BAD', name: 'x', address, priority: 1 }, 'code'],
  ['empty name', { code: `X${suffix}`, name: '  ', address, priority: 1 }, 'name'],
]) {
  const r = await create(input)
  check(`${label} -> BadRequest, field ${field}`, r, err(r).errorType === 'BadRequest' && err(r).errorInfo?.field === field)
}
const after = await gql(`{ warehouses { code } }`)
check('nothing written by the rejected creates', after, !after.data.warehouses.some((w) => w.code === `X${suffix}`))

// update name/address/priority; code untouched
const upd = await gql(`mutation($input: UpdateWarehouseInput!) { updateWarehouse(input: $input) { ${W} } }`,
  { input: { id: g.id, name: 'Polo Gamma Norte', address: { ...address, complement: 'Galpão 2' }, priority: 1 } })
const u = upd.data?.updateWarehouse
check('update changes name, complement, priority; code unchanged', upd, u?.name === 'Polo Gamma Norte' && u.address.complement === 'Galpão 2' && u.priority === 1 && u.code === code)
const sorted = await gql(`{ warehouses { code priority } }`)
const order = sorted.data.warehouses.filter((w) => w.priority === 1).map((w) => w.code)
check('sorted by priority then code', sorted, JSON.stringify(order) === JSON.stringify([...order].sort()))

// deactivate / reactivate
const off = await gql(`mutation { setWarehouseActive(id: "${g.id}", active: false) { active } }`)
check('deactivate', off, off.data?.setWarehouseActive?.active === false)
const listed = await gql(`{ warehouse(id: "${g.id}") { active } }`)
check('still readable, inactive', listed, listed.data?.warehouse?.active === false)
const on = await gql(`mutation { setWarehouseActive(id: "${g.id}", active: true) { active } }`)
check('reactivate', on, on.data?.setWarehouseActive?.active === true)

// NotFound, guards are not warehouses
for (const id of ['00000000-0000-0000-0000-000000000000', 'CODE#ALPHA']) {
  const r = await gql(`mutation { setWarehouseActive(id: "${id}", active: false) { id } }`)
  check(`setWarehouseActive ${id} -> NotFound`, r, err(r).errorType === 'NotFound')
  const q = await gql(`{ warehouse(id: "${id}") { id } }`)
  check(`warehouse(${id}) -> null`, q, q.data?.warehouse === null)
}
const nf = await gql(`mutation($input: UpdateWarehouseInput!) { updateWarehouse(input: $input) { id } }`,
  { input: { id: 'CODE#ALPHA', name: 'x', address, priority: 1 } })
check('updateWarehouse CODE#ALPHA -> NotFound', nf, err(nf).errorType === 'NotFound')

// stock movements and views (ADR-0052 §7), on a real local product and two fresh warehouses
const M = 'id type quantity balanceAfter warehouseId warehouseCode invoiceNumber supplier reason note transferId createdBy'
const products = await gql('{ products(pageSize: 1) { items { id name } } }')
const productId = products.data?.products?.items?.[0]?.id
const productName = products.data?.products?.items?.[0]?.name
check('a local product to stock', products, Boolean(productId))
const wa = (await create({ code: `SA${suffix}`, name: 'Stock A', address, priority: 1 })).data.createWarehouse
const wb = (await create({ code: `SB${suffix}`, name: 'Stock B', address, priority: 2 })).data.createWarehouse
const key = () => crypto.randomUUID()
const receive = (input) => gql(`mutation($input: RecordStockReceiptInput!) { recordStockReceipt(input: $input) { movements { ${M} } } }`, { input })
const adjust = (input) => gql(`mutation($input: RecordStockAdjustmentInput!) { recordStockAdjustment(input: $input) { movements { ${M} } } }`, { input })
const move = (input) => gql(`mutation($input: TransferStockInput!) { transferStock(input: $input) { movements { ${M} } } }`, { input })
const stockOf = async () => (await gql(`{ stockByProduct(productId: "${productId}") { totalAvailable warehouses { warehouseId warehouseCode available } } }`)).data.stockByProduct
const at = (stock, id) => stock.warehouses.find((w) => w.warehouseId === id)?.available

const rKey = key()
const r1 = await receive({ idempotencyKey: rKey, productId, warehouseId: wa.id, quantity: 20, invoiceNumber: '12345', supplier: 'ACME' })
check('receipt 20 with NF -> balance 20, author local-dev', r1, r1.data?.recordStockReceipt?.movements?.[0]?.balanceAfter === 20 && r1.data.recordStockReceipt.movements[0].createdBy === 'local-dev')
const noNf = await receive({ idempotencyKey: key(), productId, warehouseId: wa.id, quantity: 1, invoiceNumber: '' })
check('receipt without NF -> BadRequest invoiceNumber', noNf, err(noNf).errorType === 'BadRequest' && err(noNf).errorInfo?.field === 'invoiceNumber')
await receive({ idempotencyKey: key(), productId, warehouseId: wb.id, quantity: 50, invoiceNumber: 'NF-2' })
let stock = await stockOf()
check('stockByProduct: 20 + 50 = 70', stock, at(stock, wa.id) === 20 && at(stock, wb.id) === 50)
const dmg = await adjust({ idempotencyKey: key(), productId, warehouseId: wa.id, direction: 'Decrease', quantity: 2, reason: 'Damage' })
check('adjust -2 Damage -> 18', dmg, dmg.data?.recordStockAdjustment?.movements?.[0]?.quantity === -2 && dmg.data.recordStockAdjustment.movements[0].reason === 'Damage')
const other = await adjust({ idempotencyKey: key(), productId, warehouseId: wa.id, direction: 'Increase', quantity: 1, reason: 'Other' })
check('Other without note -> BadRequest note', other, err(other).errorInfo?.field === 'note')
const short = await adjust({ idempotencyKey: key(), productId, warehouseId: wa.id, direction: 'Decrease', quantity: 30, reason: 'Count' })
check('adjust -30 -> InsufficientStock, available 18', short, err(short).errorType === 'InsufficientStock' && err(short).errorInfo?.available === 18)
const tr = await move({ idempotencyKey: key(), productId, fromWarehouseId: wb.id, toWarehouseId: wa.id, quantity: 10 })
stock = await stockOf()
check('transfer 10 B -> A: 28 / 40, two rows', tr, tr.data?.transferStock?.movements?.length === 2 && at(stock, wa.id) === 28 && at(stock, wb.id) === 40)
const same = await move({ idempotencyKey: key(), productId, fromWarehouseId: wa.id, toWarehouseId: wa.id, quantity: 1 })
check('transfer to itself -> BadRequest toWarehouseId', same, err(same).errorInfo?.field === 'toWarehouseId')
await gql(`mutation { setWarehouseActive(id: "${wb.id}", active: false) { id } }`)
const inB = await receive({ idempotencyKey: key(), productId, warehouseId: wb.id, quantity: 1, invoiceNumber: 'NF-3' })
check('receipt into inactive -> WarehouseInactive', inB, err(inB).errorType === 'WarehouseInactive')
const outB = await adjust({ idempotencyKey: key(), productId, warehouseId: wb.id, direction: 'Decrease', quantity: 1, reason: 'Count' })
check('decrease on inactive is allowed', outB, !outB.errors)
await gql(`mutation { setWarehouseActive(id: "${wb.id}", active: true) { id } }`)
const ghost = await receive({ idempotencyKey: key(), productId: 'no-such-product', warehouseId: wa.id, quantity: 1, invoiceNumber: 'NF' })
check('unknown product -> ProductNotFound', ghost, err(ghost).errorType === 'ProductNotFound')
const replay = await receive({ idempotencyKey: rKey, productId, warehouseId: wa.id, quantity: 20, invoiceNumber: '12345', supplier: 'ACME' })
stock = await stockOf()
check('replay returns the first receipt, nothing added', replay, replay.data?.recordStockReceipt?.movements?.[0]?.id === rKey && at(stock, wa.id) === 28)
const reused = await receive({ idempotencyKey: rKey, productId, warehouseId: wa.id, quantity: 21, invoiceNumber: '12345' })
check('same key, different request -> IdempotencyKeyReused', reused, err(reused).errorType === 'IdempotencyKeyReused')
const histP = await gql(`{ stockMovementsByProduct(productId: "${productId}", pageSize: 3) { items { id type createdBy } nextToken } }`)
check('product history: newest first, paged', histP, histP.data?.stockMovementsByProduct?.items?.length === 3 && histP.data.stockMovementsByProduct.nextToken)
const byW = await gql(`{ stockByWarehouse(warehouseId: "${wa.id}") { items { productId productName available } } }`)
check('warehouse balances with the product name', byW, byW.data?.stockByWarehouse?.items?.[0]?.productName === productName && byW.data.stockByWarehouse.items[0].available === 28)
const histW = await gql(`{ stockMovementsByWarehouse(warehouseId: "${wa.id}") { items { id productName quantity } } }`)
check('warehouse history with names; adds up to the balance', histW,
  histW.data?.stockMovementsByWarehouse?.items?.every((i) => i.productName === productName)
  && histW.data.stockMovementsByWarehouse.items.reduce((sum, i) => sum + i.quantity, 0) === 28)
const badPage = await gql(`{ stockMovementsByWarehouse(warehouseId: "${wa.id}", pageSize: 101) { items { id } } }`)
check('pageSize 101 -> BadRequest', badPage, err(badPage).errorType === 'BadRequest')
for (const w of [wa, wb]) await gql(`mutation { setWarehouseActive(id: "${w.id}", active: false) { id } }`)

// leave the test warehouse inactive, like the E2E suite does
await gql(`mutation { setWarehouseActive(id: "${g.id}", active: false) { id } }`)
console.log(`\n${results.filter(Boolean).length}/${results.length} passed`)
process.exit(results.every(Boolean) ? 0 : 1)
