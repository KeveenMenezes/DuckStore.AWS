import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: GetItem on Pricing's "product-discounts" projection by
// ProductId. Replaces couponFor (ADR-0026). Expiry is checked here, at read time, rather than by
// any scheduler — a discount past EndsAt is treated as if it doesn't exist.
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { ProductId: util.dynamodb.toDynamoDB(ctx.args.productId) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const item = ctx.result
  if (!item) return null

  const now = util.time.nowISO8601()
  if (item.EndsAt < now || item.StartsAt > now) return null

  return {
    productId: item.ProductId,
    campaignId: item.CampaignId,
    discountType: item.DiscountType,
    value: +item.Value,
    startsAt: item.StartsAt,
    endsAt: item.EndsAt,
  }
}
