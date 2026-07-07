import { util } from '@aws-appsync/utils'

// Pipeline function 1/2 (ADR-0029): GetItem on the composite Id `${productId}#${base64(userName)}`
// — one row per (productId, userName), so this recovers the existing review (if any) to preserve
// its original CreatedAt in function 2. Stashes both the computed id and the existing item.
export function request(ctx) {
  const { productId, userName } = ctx.args.input
  const id = `${productId}#${util.base64Encode(userName)}`
  ctx.stash.id = id
  return {
    operation: 'GetItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  ctx.stash.existing = ctx.result ?? null
  return ctx.result
}
