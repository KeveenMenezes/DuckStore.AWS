import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct GetItem resolver (ADR-0009). Null for an unknown id and
// for a CODE#<code> guard, so a guard key can never be read back as a warehouse.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'GetItem',
    key: util.dynamodb.toMapValues({ WarehouseId: ctx.args.id }),
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const item = ctx.result
  if (!item || item.Type !== 'Warehouse') return null
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
