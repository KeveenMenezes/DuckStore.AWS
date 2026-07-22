import { util } from '@aws-appsync/utils'

// Pipeline function 1/2: GetItem on the composite Id `${productId}#${ctx.identity.sub}` — one row
// per (productId, authenticated user). The Cognito sub is derived server-side, never taken from
// client input, so a client can no longer overwrite another user's review by supplying their
// display name (the prior base64(userName) scheme's key was fully client-controlled). Stashes the
// computed id and the existing item so function 2 can preserve CreatedAt on an edit.
export function request(ctx) {
  if (!ctx.identity?.sub) util.unauthorized()

  const { productId } = ctx.args.input
  const id = `${productId}#${ctx.identity.sub}`
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
