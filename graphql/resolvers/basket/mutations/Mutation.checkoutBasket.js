import { util } from '@aws-appsync/utils'

// Top-level pipeline resolver for checkoutBasket (ADR-0048 §4), run on every checkout, with or
// without points. ADR-0009 criterion 2 — orchestration across services: the steps only map
// arguments, results and errors; every price rule lives in the Pricing Lambda.
//   1. Mutation.checkoutBasket.loadCart.js  (shopping-carts)  — the saved cart's items
//   2. Mutation.checkoutBasket.quote.js     (Pricing)         — cap + total charged, or PriceChanged
//   3. Mutation.checkoutBasket.checkout.js  (Basket)          — checkout with the server's total
// Cognito-only: the owner always comes from the token.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()
  return {}
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.prev.result
}
