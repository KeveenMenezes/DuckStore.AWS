import { util } from '@aws-appsync/utils'

// Public (API_KEY or Cognito). Invokes Pricing's GetBasketInstallmentPlan Lambda — sums
// cost/originalPrice across every cart item and runs the whole cart through the same
// cost-floor calculation as a single checkout transaction (ADR-0009). A positive pointsToUse is
// Cognito-only (ADR-0048 §3) — a guest (API_KEY) sending it is rejected rather than silently
// ignored, since that would otherwise look like the discount was applied when it was not. The cap
// and the conversion are Pricing's; this only maps.
export function request(ctx) {
  const payload = {
    Items: ctx.args.items.map(i => ({ ProductId: i.productId, Quantity: i.quantity })),
  }

  const pointsToUse = ctx.args.pointsToUse ?? 0
  if (pointsToUse > 0) {
    if (!ctx.identity || !ctx.identity.sub) util.unauthorized()
    payload.PointsToUse = pointsToUse
  }

  return { operation: 'Invoke', payload }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  // PointsAboveCap comes back as data, not as a Lambda failure, so its type and cap survive
  // (see the Pricing endpoint); re-raised here as a typed GraphQL error.
  if (r.Rejection) {
    util.error(r.Rejection.Message, r.Rejection.ErrorType, null, {
      serverTotal: r.Rejection.ServerTotal ?? null,
      maxRedeemablePoints: r.Rejection.MaxRedeemablePoints ?? null,
    })
  }
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
    maxRedeemablePoints: r.MaxRedeemablePoints ?? 0,
    pointsDiscount: r.PointsDiscount ?? 0,
  }
}
