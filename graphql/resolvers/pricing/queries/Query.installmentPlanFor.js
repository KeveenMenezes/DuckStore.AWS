import { util } from '@aws-appsync/utils'

// Public (API_KEY or Cognito). Invokes Pricing's GetInstallmentPlan Lambda — a non-trivial
// calculation, not a key lookup, so it stays a Lambda resolver (ADR-0009).
export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { ProductId: ctx.args.productId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    productId: r.ProductId,
    originalPrice: r.OriginalPrice,
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
