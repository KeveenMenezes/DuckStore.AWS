import { util } from '@aws-appsync/utils'

// Pipeline step 1 of checkoutBasket (ADR-0048 §4): reads the caller's saved cart, so Pricing
// quotes the items that will actually be checked out — never items sent by the client.
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { OwnerId: util.dynamodb.toDynamoDB(`USER#${ctx.identity.sub}`) },
    consistentRead: true,
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  // The cart is a JSON blob in Data (PascalCase from the .NET serializer), as in Query.basket.js.
  const cart = ctx.result && ctx.result.Data ? JSON.parse(ctx.result.Data) : null
  const items = cart && cart.Items ? cart.Items : []
  if (items.length === 0) util.error('There is no cart to check out.', 'BadRequest')

  ctx.stash.cartItems = items.map(i => ({ ProductId: `${i.ProductId}`, Quantity: i.Quantity }))
  return ctx.stash.cartItems
}
