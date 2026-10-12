import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct Scan resolver (ADR-0009 — same shape as `orders`): the
// table also holds CODE#<code> guards, so the Type filter is mandatory, never optional. Tens of
// warehouses, so one page is the whole table; sorted here by priority, then code — the order a sale
// is allocated in (SPEC-inventory.md, module 4).
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'Scan',
    filter: {
      expression: '#type = :warehouse',
      expressionNames: { '#type': 'Type' },
      expressionValues: util.dynamodb.toMapValues({ ':warehouse': 'Warehouse' }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  // One Scan page holds about a thousand warehouses (1 MB). Past that the list would be cut short
  // and sorted only within the page, so say so instead of returning it silently (ADR-0052 §4).
  if (ctx.result.nextToken) {
    util.appendError('The warehouse list is larger than one Scan page and was truncated', 'Truncated')
  }

  // Array.prototype.sort takes no comparator in APPSYNC_JS (an inline one fails its linter; a named
  // one was silently ignored), so sort a string key instead: priority zero-padded to 3 digits
  // (1-999), then code, then id for uniqueness.
  const byKey = {}
  const keys = []
  for (const item of ctx.result.items ?? []) {
    const key = `${`00${item.Priority}`.slice(-3)}#${item.Code}#${item.WarehouseId}`
    byKey[key] = toWarehouse(item)
    keys.push(key)
  }
  keys.sort()
  return keys.map((key) => byKey[key])
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
