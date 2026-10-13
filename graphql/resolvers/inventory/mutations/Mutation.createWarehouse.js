import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct TransactWriteItems resolver (ADR-0009, criteria answered
// in ADR-0052 §4): the warehouse and its CODE#<code> guard are written together, both
// attribute_not_exists, so a taken code cancels the whole write. The guard is what makes the code
// unique, not a read before the write.
//
// Validation rules: the same as Mutation.updateWarehouse.js (APPSYNC_JS can't share modules), mirrored
// by local.ts (lib/warehouse-validation.ts, with tests) and the Blazor WarehouseForm. Change all four.
// Patterns go through util.matches rather than regex literals; Number.isInteger doesn't exist here.
const TABLE = 'inventory-warehouses'
const STATES = [
  'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA',
  'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO',
]

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  const input = ctx.args.input
  const code = normalizeCode(input.code)
  const fields = normalizeFields(input)
  const id = util.autoId()
  const now = util.time.nowISO8601()

  const item = {
    Type: 'Warehouse',
    Code: code,
    Name: fields.name,
    Street: fields.street,
    Number: fields.number,
    District: fields.district,
    City: fields.city,
    State: fields.state,
    ZipCode: fields.zipCode,
    Priority: fields.priority,
    Active: true,
    CreatedAt: now,
    UpdatedAt: now,
    UpdatedBy: ctx.identity.sub,
  }
  if (fields.complement) item.Complement = fields.complement
  ctx.stash.item = { WarehouseId: id, ...item }

  return {
    operation: 'TransactWriteItems',
    transactItems: [
      {
        table: TABLE,
        operation: 'PutItem',
        key: util.dynamodb.toMapValues({ WarehouseId: id }),
        attributeValues: util.dynamodb.toMapValues(item),
        condition: { expression: 'attribute_not_exists(WarehouseId)' },
      },
      {
        table: TABLE,
        operation: 'PutItem',
        key: util.dynamodb.toMapValues({ WarehouseId: `CODE#${code}` }),
        attributeValues: util.dynamodb.toMapValues({ Type: 'CodeGuard', TargetWarehouseId: id }),
        condition: { expression: 'attribute_not_exists(WarehouseId)' },
      },
    ],
  }
}

// Item 1 is the guard: its condition failing means the code is taken. Two creates of one code at
// the same instant can instead cancel with TransactionConflict; a retry then sees the guard.
export function response(ctx) {
  if (ctx.error) {
    if (ctx.error.type === 'DynamoDB:TransactionCanceledException') {
      const reasons = ctx.result?.cancellationReasons ?? []
      if (reasons.length > 1 && reasons[1].type === 'ConditionalCheckFailed') {
        util.error(`Warehouse code ${ctx.stash.item.Code} is already taken`, 'WarehouseCodeTaken', null, { field: 'code' })
      }
      if (reasons.some((reason) => reason.type === 'TransactionConflict')) {
        util.error('Another write to this warehouse code is in progress; retry', 'TransactionConflict')
      }
    }
    util.error(ctx.error.message, ctx.error.type)
  }

  return toWarehouse(ctx.stash.item)
}

function toWarehouse(item) {
  return {
    id: item.WarehouseId,
    code: item.Code,
    name: item.Name,
    address: {
      street: item.Street,
      number: item.Number,
      complement: item.Complement ?? null,
      district: item.District,
      city: item.City,
      state: item.State,
      zipCode: item.ZipCode,
    },
    priority: item.Priority,
    active: item.Active,
    createdAt: item.CreatedAt,
    updatedAt: item.UpdatedAt,
  }
}

function badRequest(field, message) {
  util.error(message, 'BadRequest', null, { field })
}

function normalizeCode(code) {
  const normalized = (code ?? '').trim().toUpperCase()
  if (!util.matches('^[A-Z0-9][A-Z0-9-]{1,19}$', normalized)) {
    badRequest('code', 'code must be 2-20 characters: letters, digits or hyphens, starting with a letter or digit')
  }
  return normalized
}

function checkLength(value, field, min, max) {
  const label = field.split('.').pop()
  if (value.length < min || value.length > max) {
    badRequest(field, min === 0 ? `${label} must be at most ${max} characters` : `${label} must be ${min}-${max} characters`)
  }
  return value
}

function normalizeFields(input) {
  const address = input.address ?? {}
  const name = checkLength((input.name ?? '').trim(), 'name', 1, 80)
  const street = checkLength((address.street ?? '').trim(), 'address.street', 1, 120)
  const number = checkLength((address.number ?? '').trim(), 'address.number', 1, 10)
  const complement = checkLength((address.complement ?? '').trim(), 'address.complement', 0, 60)
  const district = checkLength((address.district ?? '').trim(), 'address.district', 1, 60)
  const city = checkLength((address.city ?? '').trim(), 'address.city', 1, 60)

  const state = (address.state ?? '').trim().toUpperCase()
  if (!STATES.includes(state)) badRequest('address.state', 'state must be a Brazilian UF, e.g. SP')

  const zipCode = (address.zipCode ?? '').trim().split('-').join('')
  if (!util.matches('^[0-9]{8}$', zipCode)) badRequest('address.zipCode', 'zipCode must have 8 digits (00000-000)')

  // Math.floor, not Number.isInteger: APPSYNC_JS doesn't implement Number.isInteger.
  const priority = input.priority
  if (Math.floor(priority) !== priority || priority < 1 || priority > 999) {
    badRequest('priority', 'priority must be an integer between 1 and 999')
  }

  return { name, street, number, complement, district, city, state, zipCode, priority }
}
