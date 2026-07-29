import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  const { pageSize = 10, nextToken } = ctx.args
  return {
    operation: 'Scan',
    limit: pageSize,
    nextToken,
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    items: ctx.result.items.map(item => ({
      id: item.Id,
      name: item.Name,
      discountType: item.DiscountType,
      value: item.Value,
      startsAt: item.StartsAt,
      endsAt: item.EndsAt,
      productIds: item.ProductIds ?? [],
      status: item.Status,
    })),
    nextToken: ctx.result.nextToken ?? null,
  }
}
