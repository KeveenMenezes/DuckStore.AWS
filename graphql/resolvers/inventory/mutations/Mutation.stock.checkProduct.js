import { util, runtime } from '@aws-appsync/utils'

// Pipeline step on Catalog's products table (read only), shared by recordStockReceipt and
// recordStockAdjustment (ADR-0052 §7). Only movements that bring stock in check the product: a
// product deleted from Catalog can still be adjusted down to zero, never restocked.
export function request(ctx) {
  const input = ctx.args.input
  if (input.direction === 'Decrease') return runtime.earlyReturn({})

  return {
    operation: 'GetItem',
    key: util.dynamodb.toMapValues({ Id: input.productId }),
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) util.error('Product not found', 'ProductNotFound', null, { field: 'productId' })
  return {}
}
