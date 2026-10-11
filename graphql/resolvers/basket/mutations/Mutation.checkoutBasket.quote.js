import { util } from '@aws-appsync/utils'

// Pipeline step 2 of checkoutBasket (ADR-0048 §4): Pricing quotes the saved cart with the points
// and the payment choice, checks the cap, picks the total charged for that choice and compares it
// with the total the customer confirmed (input.totalPrice). Every one of those decisions is
// Pricing's; this only maps. A refusal comes back as data (Rejection) and is raised as a typed
// GraphQL error, so the SPA can branch on PriceChanged/PointsAboveCap and read serverTotal.
export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      Items: ctx.stash.cartItems,
      PointsToUse: input.pointsToUse ?? 0,
      ExpectedTotal: input.totalPrice,
      PaymentMethod: input.paymentMethod,
      Installments: input.installments,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  if (r.Rejection) {
    util.error(r.Rejection.Message, r.Rejection.ErrorType, null, {
      serverTotal: r.Rejection.ServerTotal ?? null,
      maxRedeemablePoints: r.Rejection.MaxRedeemablePoints ?? null,
    })
  }

  ctx.stash.chargedTotal = r.ChargedTotal
  return { chargedTotal: r.ChargedTotal }
}
