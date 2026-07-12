import { util } from '@aws-appsync/utils'

// Direct DynamoDB PutItem resolver (ADR-0009) — no business logic beyond mapping/upserting, so no
// Lambda is needed. The whole cart (including items) is stored as a single JSON blob in the `Data`
// attribute — this mirrors Basket.Function's (now-removed) ShoppingCartSerializer contract exactly
// (PascalCase field names: OwnerId/Items/TotalPrice, and per item Quantity/Color/Price/ProductId/
// ProductName/ImageUrl) so `basket`/`deleteBasket` and CDC consumers of this table keep working.
const GUEST_CART_TTL_SECONDS = 15 * 24 * 60 * 60

// Authenticated → derive USER#<sub> from the token (ignore the client value).
// Guest (API key, injected by the BFF) → trust only a GUEST#-prefixed ownerId.
function resolveOwnerId(ctx) {
  if (ctx.identity && ctx.identity.sub) return `USER#${ctx.identity.sub}`
  const ownerId = ctx.args.ownerId
  if (!ownerId || !ownerId.startsWith('GUEST#')) util.unauthorized()
  return ownerId
}

export function request(ctx) {
  const ownerId = resolveOwnerId(ctx)
  const input = ctx.args.input

  const items = input.items.map(i => ({
    Quantity: i.quantity,
    Color: i.color ?? '',
    Price: i.price,
    ProductId: i.productId,
    ProductName: i.productName,
    ImageUrl: i.imageUrl ?? '',
  }))
  const totalPrice = items.reduce((sum, item) => sum + item.Price * item.Quantity, 0)

  const attributeValues = {
    OwnerId: ownerId,
    Data: JSON.stringify({ OwnerId: ownerId, Items: items, TotalPrice: totalPrice }),
  }

  // Only guest carts expire; user carts omit ExpiresAt so DynamoDB TTL never touches them.
  if (ownerId.startsWith('GUEST#')) {
    attributeValues.ExpiresAt = util.time.nowEpochSeconds() + GUEST_CART_TTL_SECONDS
  }

  return {
    operation: 'PutItem',
    key: { OwnerId: util.dynamodb.toDynamoDB(ownerId) },
    attributeValues: util.dynamodb.toMapValues(attributeValues),
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { ownerId: ctx.result.OwnerId }
}
