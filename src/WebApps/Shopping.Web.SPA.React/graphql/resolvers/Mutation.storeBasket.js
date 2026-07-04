import { util } from '@aws-appsync/utils'

// Authenticated → derive USER#<sub> from the token (ignore the client value).
// Guest (API key, injected by the BFF) → trust only a GUEST#-prefixed ownerId.
function resolveOwnerId(ctx) {
  if (ctx.identity && ctx.identity.sub) return `USER#${ctx.identity.sub}`
  const ownerId = ctx.args.ownerId
  if (!ownerId || !ownerId.startsWith('GUEST#')) util.unauthorized()
  return ownerId
}

export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      Cart: {
        OwnerId: resolveOwnerId(ctx),
        Items: input.items.map(i => ({
          Quantity: i.quantity,
          Color: i.color ?? '',
          Price: i.price,
          ProductId: i.productId,
          ProductName: i.productName,
          ImageUrl: i.imageUrl ?? '',
        })),
      },
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { ownerId: ctx.result.OwnerId }
}
