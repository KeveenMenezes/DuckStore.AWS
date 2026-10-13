import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Pipeline (ADR-0052 §7): Mutation.stock.checkProduct.js checks
// the product exists in Catalog, then Mutation.recordStockAdjustment.record.js invokes the Lambda that applies the
// stock rules. The group check runs here, before either step.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()
  return {}
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.prev.result
}
