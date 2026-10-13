import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct Query on inventory-stock by product (ADR-0052 §7): one row
// per warehouse that ever held it, inactive ones included, so not paginated; totalAvailable sums them.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'Query',
    query: {
      expression: 'ProductId = :productId',
      expressionValues: util.dynamodb.toMapValues({ ':productId': ctx.args.productId }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  let totalAvailable = 0
  const warehouses = []
  for (const item of ctx.result.items ?? []) {
    totalAvailable += item.Available
    warehouses.push({
      warehouseId: item.WarehouseId,
      warehouseCode: item.WarehouseCode,
      available: item.Available,
      reserved: item.Reserved ?? 0,
    })
  }
  return { productId: ctx.args.productId, totalAvailable, warehouses }
}
