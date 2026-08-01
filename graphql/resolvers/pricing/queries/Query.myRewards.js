import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver — Cognito only. Query on OwnerId, filtered to Status=Issued
// and not expired (ADR-0046 §4) — expiry is checked here and is authoritative; the table's TTL on
// ExpiresAt only clears dead rows eventually (ADR-0044 precedent), so a coupon past its expiry but
// not yet swept must never appear here.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'Query',
    query: {
      expression: 'OwnerId = :ownerId',
      expressionValues: util.dynamodb.toMapValues({ ':ownerId': `USER#${ctx.identity.sub}` }),
    },
    filter: {
      expression: '#status = :issued AND ExpiresAt > :now',
      expressionNames: { '#status': 'Status' },
      expressionValues: util.dynamodb.toMapValues({
        ':issued': 'Issued',
        ':now': Math.floor(util.time.nowEpochSeconds()),
      }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return (ctx.result.items ?? []).map(item => ({
    id: item.DiscountId,
    amount: item.Amount,
    status: item.Status,
    // ExpiresAt is stored in epoch *seconds* (DynamoDB TTL's unit); APPSYNC_JS only converts
    // from milliseconds, hence the ×1000.
    expiresAt: util.time.epochMilliSecondsToISO8601(item.ExpiresAt * 1000),
    sourceRedemptionId: item.SourceRedemptionId,
  }))
}
