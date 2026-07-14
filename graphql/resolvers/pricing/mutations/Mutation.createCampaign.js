import { util } from '@aws-appsync/utils'

// Admin only. Invokes Pricing's CreateCampaign Lambda — fans out a TransactWriteItems across
// campaigns + product-discounts server-side (ADR-0026).
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  const { name, discountType, value, startsAt, endsAt, productIds } = ctx.args
  return {
    operation: 'Invoke',
    payload: {
      Name: name,
      DiscountType: discountType,
      Value: value,
      StartsAt: startsAt,
      EndsAt: endsAt,
      ProductIds: productIds,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    id: r.Id,
    name: ctx.args.name,
    discountType: ctx.args.discountType,
    value: ctx.args.value,
    startsAt: ctx.args.startsAt,
    endsAt: ctx.args.endsAt,
    productIds: ctx.args.productIds,
  }
}
