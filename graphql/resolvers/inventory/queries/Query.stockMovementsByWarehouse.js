import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct pipeline (ADR-0052 §7): Query.stockMovementsByWarehouse.movements.js reads GSI1 of inventory-movements, the warehouse's history newest first, then
// Query.stock.productNames.js adds the product names from Catalog. The group check and the page size
// are checked here, before either step.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()
  pageSizeOf(ctx)
  return {}
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.prev.result
}

// pageSize 1-100 (default 20): a page never exceeds BatchGetItem's 100 keys (ADR-0052 §7).
function pageSizeOf(ctx) {
  const size = ctx.args.pageSize ?? 20
  if (Math.floor(size) !== size || size < 1 || size > 100) {
    util.error('pageSize must be an integer between 1 and 100', 'BadRequest', null, { field: 'pageSize' })
  }
  return size
}
