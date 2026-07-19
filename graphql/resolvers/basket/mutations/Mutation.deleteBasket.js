import { util } from '@aws-appsync/utils'

// Authenticated → derive USER#<sub> from the token (ignore the client value).
// Guest (API key, injected by the BFF) → trust only a GUEST#-prefixed ownerId.
// Duplicated verbatim in Query.basket.js and Mutation.storeBasket.js — AppSync JS resolvers are
// inlined standalone (no bundler/import resolution, see appsync-api.ts's `resolver()`), so keep
// all three copies in sync.
function resolveOwnerId(ctx) {
  if (ctx.identity && ctx.identity.sub) return `USER#${ctx.identity.sub}`
  const ownerId = ctx.args.ownerId
  if (!ownerId || !ownerId.startsWith('GUEST#')) util.unauthorized()
  return ownerId
}

export function request(ctx) {
  return {
    operation: 'DeleteItem',
    key: { OwnerId: util.dynamodb.toDynamoDB(resolveOwnerId(ctx)) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: true }
}
