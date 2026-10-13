import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct UpdateItem resolver (ADR-0009): name, address and
// priority only — the code is immutable (its CODE#<code> guard would otherwise go stale) and Active
// has its own mutation. The condition keeps an unknown id or a guard key from being upserted.
//
// Validation rules: the same as Mutation.createWarehouse.js (APPSYNC_JS can't share modules), mirrored
// by local.ts (lib/warehouse-validation.ts, with tests) and the Blazor WarehouseForm. Change all four.
const STATES = [
  'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA',
  'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO',
]

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  const input = ctx.args.input
  const fields = normalizeFields(input)
  const values = {
    ':name': fields.name,
    ':street': fields.street,
    ':number': fields.number,
    ':district': fields.district,
    ':city': fields.city,
    ':state': fields.state,
    ':zipCode': fields.zipCode,
    ':priority': fields.priority,
    ':now': util.time.nowISO8601(),
    ':by': ctx.identity.sub,
  }
  let expression = 'SET #name = :name, Street = :street, #number = :number, District = :district, City = :city, '
    + '#state = :state, ZipCode = :zipCode, Priority = :priority, UpdatedAt = :now, UpdatedBy = :by'
  if (fields.complement) {
    expression += ', Complement = :complement'
    values[':complement'] = fields.complement
  } else {
    expression += ' REMOVE Complement'
  }

  return {
    operation: 'UpdateItem',
    key: util.dynamodb.toMapValues({ WarehouseId: input.id }),
    update: {
      expression,
      // Name, Number and State are DynamoDB reserved words.
      expressionNames: { '#name': 'Name', '#number': 'Number', '#state': 'State' },
      expressionValues: util.dynamodb.toMapValues(values),
    },
    condition: {
      expression: 'attribute_exists(WarehouseId) AND #type = :warehouse',
      expressionNames: { '#type': 'Type' },
      expressionValues: util.dynamodb.toMapValues({ ':warehouse': 'Warehouse' }),
    },
  }
}

// UpdateItem returns the full item after the write (ALL_NEW).
export function response(ctx) {
  if (ctx.error) {
    if (ctx.error.type === 'DynamoDB:ConditionalCheckFailedException') {
      util.error('Warehouse not found', 'NotFound')
    }
    util.error(ctx.error.message, ctx.error.type)
  }

  return toWarehouse(ctx.result)
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
