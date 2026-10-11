import { util } from '@aws-appsync/utils'

// Cognito-only. Direct DynamoDB GetItem (ADR-0009) on the caller's own row,
// `${productId}#${ctx.identity.sub}` (ADR-0037) — the sub is derived server-side, never taken
// from input. Returns null when there's no row (the customer never bought the product, or a
// Deleted row's TTL already expired), otherwise the row with its status — Eligible, Published or
// Deleted (ADR-0049 §4). An Eligible row carries no Rating/Comment/UserName yet, so they fall back
// to 0/''/'' to honour Review's non-null fields; rows without Status are legacy = Published.
export function request(ctx) {
  if (!ctx.identity?.sub) util.unauthorized()

  const id = `${ctx.args.productId}#${ctx.identity.sub}`
  return {
    operation: 'GetItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const item = ctx.result
  if (!item) return null

  return {
    id: item.Id,
    productId: item.ProductId,
    userName: item.UserName ?? '',
    rating: Math.floor(+(item.Rating ?? 0)),
    comment: item.Comment ?? '',
    createdAt: item.CreatedAt,
    updatedAt: item.UpdatedAt ?? item.CreatedAt,
    status: item.Status ?? 'Published',
  }
}
