import { util } from '@aws-appsync/utils'

// Public (API_KEY or Cognito). Invokes Pricing's GetBasketInstallmentPlan Lambda — sums
// cost/originalPrice across every cart item and runs the whole cart through the same
// cost-floor calculation as a single checkout transaction (ADR-0009).
export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: {
      Items: ctx.args.items.map(i => ({ ProductId: i.productId, Quantity: i.quantity })),
    },
  }
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
