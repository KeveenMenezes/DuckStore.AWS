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
  return {
    operation: 'GetItem',
    key: { OwnerId: util.dynamodb.toDynamoDB(resolveOwnerId(ctx)) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

  // The basket is stored as a JSON blob in the Data attribute (PascalCase from .NET serializer).
  const cart = JSON.parse(ctx.result.Data)
  return {
    ownerId: cart.OwnerId,
    items: (cart.Items ?? []).map(i => ({
      quantity: i.Quantity,
      color: i.Color ?? null,
      price: i.Price,
      productId: `${i.ProductId}`,
      productName: i.ProductName,
      imageId: i.ImageId ?? null,
    })),
    totalPrice: cart.TotalPrice,
  }
}
