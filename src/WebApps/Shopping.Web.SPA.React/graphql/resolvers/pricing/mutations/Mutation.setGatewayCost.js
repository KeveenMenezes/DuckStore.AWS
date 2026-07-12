import { util } from '@aws-appsync/utils'

// Admin only. Direct DynamoDB UpdateItem resolver (ADR-0009) — configures a payment-gateway
// provider's flat fee, à vista rate, and per-installment-count rate table (ADR-0028). Same
// single-`UpdatedAt`-attribute contract as `prices` (no separate CreatedAt persisted — see
// DynamoGatewayCostRepository.ToItem, now removed), so a plain UpdateItem upsert suffices.
// installmentRates arrives as AWSJSON (a plain JSON object with string keys, e.g.
// {"1": 3.0, "2": 4.2}) and maps straight onto the native DynamoDB Map attribute.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'UpdateItem',
    key: { Provider: util.dynamodb.toDynamoDB(ctx.args.provider) },
    update: {
      expression:
        'SET FlatFeePerTransaction = :flatFee, AvistaRatePercent = :avista, InstallmentRates = :rates, UpdatedAt = :now',
      expressionValues: util.dynamodb.toMapValues({
        ':flatFee': ctx.args.flatFeePerTransaction,
        ':avista': ctx.args.avistaRatePercent,
        ':rates': ctx.args.installmentRates,
        ':now': util.time.nowISO8601(),
      }),
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
