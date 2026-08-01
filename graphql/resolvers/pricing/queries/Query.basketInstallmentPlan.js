import { util } from '@aws-appsync/utils'

// Public (API_KEY or Cognito). Invokes Pricing's GetBasketInstallmentPlan Lambda — sums
// cost/originalPrice across every cart item and runs the whole cart through the same
// cost-floor calculation as a single checkout transaction (ADR-0009). discountId is optional and
// Cognito-only (ADR-0046 §5) — a guest (API_KEY) sending one is rejected rather than silently
// ignored, since that would otherwise look like the coupon was applied when it was not.
export function request(ctx) {
  const payload = {
    Items: ctx.args.items.map(i => ({ ProductId: i.productId, Quantity: i.quantity })),
  }

  if (ctx.args.discountId) {
    if (!ctx.identity || !ctx.identity.sub) util.unauthorized()
    payload.OwnerId = `USER#${ctx.identity.sub}`
    payload.DiscountId = ctx.args.discountId
  }

  return { operation: 'Invoke', payload }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    totalOriginalPrice: r.TotalOriginalPrice,
    price: r.Price,
    cashPrice: r.CashPrice,
    maxInstallmentsWithoutInterest: r.MaxInstallmentsWithoutInterest,
    installments: (r.InstallmentPlan ?? []).map(e => ({
      count: e.Count,
      value: e.Value,
      totalValue: e.TotalValue,
      hasInterest: e.HasInterest,
    })),
  }
}
