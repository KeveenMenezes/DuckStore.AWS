// SPEC-inventory-warehouses criteria 2-7 and 9 against the local BFF (local.ts + DynamoDB Local).
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

// 2. seeded ALPHA/BETA listed, active, sorted
const list0 = await gql(`{ warehouses { ${W} } }`)
const codes0 = list0.data?.warehouses?.map((w) => w.code) ?? []
check('seeded ALPHA and BETA listed first, active', list0, codes0[0] === 'ALPHA' && codes0[1] === 'BETA' && list0.data.warehouses.slice(0, 2).every((w) => w.active))

const created = await create({ code: ` ${code.toLowerCase()} `, name: 'Polo Gamma', address, priority: 3 })
const g = created.data?.createWarehouse
check('create normalizes code, UF, CEP and starts active', created, g?.code === code && g.address.state === 'PE' && g.address.zipCode === '50050000' && g.active === true && g.address.complement === null)

// 3. taken code
const taken = await create({ code: code.toLowerCase(), name: 'Outro', address, priority: 4 })
check('same code (lowercase) -> WarehouseCodeTaken, field code', taken, err(taken).errorType === 'WarehouseCodeTaken' && err(taken).errorInfo?.field === 'code')

// 4. invalid input
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

// 5. update name/address/priority; code untouched
const upd = await gql(`mutation($input: UpdateWarehouseInput!) { updateWarehouse(input: $input) { ${W} } }`,
  { input: { id: g.id, name: 'Polo Gamma Norte', address: { ...address, complement: 'Galpão 2' }, priority: 1 } })
const u = upd.data?.updateWarehouse
check('update changes name, complement, priority; code unchanged', upd, u?.name === 'Polo Gamma Norte' && u.address.complement === 'Galpão 2' && u.priority === 1 && u.code === code)
const sorted = await gql(`{ warehouses { code priority } }`)
const order = sorted.data.warehouses.filter((w) => w.priority === 1).map((w) => w.code)
check('sorted by priority then code', sorted, JSON.stringify(order) === JSON.stringify([...order].sort()))

// 6. deactivate / reactivate
const off = await gql(`mutation { setWarehouseActive(id: "${g.id}", active: false) { active } }`)
check('deactivate', off, off.data?.setWarehouseActive?.active === false)
const listed = await gql(`{ warehouse(id: "${g.id}") { active } }`)
check('still readable, inactive', listed, listed.data?.warehouse?.active === false)
const on = await gql(`mutation { setWarehouseActive(id: "${g.id}", active: true) { active } }`)
check('reactivate', on, on.data?.setWarehouseActive?.active === true)

// 7. NotFound, guards are not warehouses
for (const id of ['00000000-0000-0000-0000-000000000000', 'CODE#ALPHA']) {
  const r = await gql(`mutation { setWarehouseActive(id: "${id}", active: false) { id } }`)
  check(`setWarehouseActive ${id} -> NotFound`, r, err(r).errorType === 'NotFound')
  const q = await gql(`{ warehouse(id: "${id}") { id } }`)
  check(`warehouse(${id}) -> null`, q, q.data?.warehouse === null)
}
const nf = await gql(`mutation($input: UpdateWarehouseInput!) { updateWarehouse(input: $input) { id } }`,
  { input: { id: 'CODE#ALPHA', name: 'x', address, priority: 1 } })
check('updateWarehouse CODE#ALPHA -> NotFound', nf, err(nf).errorType === 'NotFound')

// leave the test warehouse inactive, like the E2E suite does
await gql(`mutation { setWarehouseActive(id: "${g.id}", active: false) { id } }`)
console.log(`\n${results.filter(Boolean).length}/${results.length} passed`)
process.exit(results.every(Boolean) ? 0 : 1)
