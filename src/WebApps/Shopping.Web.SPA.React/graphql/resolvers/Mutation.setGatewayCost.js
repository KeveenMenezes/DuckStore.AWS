import { util } from '@aws-appsync/utils'

// Admin only. Invokes Pricing's SetGatewayCost Lambda — configures a payment-gateway provider's
// flat fee, à vista rate, and per-installment-count rate table (ADR-0028). installmentRates
// arrives as AWSJSON (a plain JSON object with string keys, e.g. {"1": 3.0, "2": 4.2}) and is
// passed straight through to the Lambda payload.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: {
      Provider: ctx.args.provider,
      FlatFeePerTransaction: ctx.args.flatFeePerTransaction,
      AvistaRatePercent: ctx.args.avistaRatePercent,
      InstallmentRates: ctx.args.installmentRates,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    provider: r.Provider,
    flatFeePerTransaction: r.FlatFeePerTransaction,
    avistaRatePercent: r.AvistaRatePercent,
    installmentRates: r.InstallmentRates,
  }
}
