import { util } from '@aws-appsync/utils'

// Admin/Seller only. Invokes Pricing's SetNominalPrice Lambda — decoupled from
// createProduct/updateProduct (ADR-0026): Catalog no longer stores price.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: {
      ProductId: ctx.args.productId,
      NominalPrice: ctx.args.price,
      Cost: ctx.args.cost,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    productId: r.ProductId,
    nominalPrice: r.NominalPrice,
    cost: r.Cost,
    updatedAt: util.time.nowISO8601(),
  }
}
