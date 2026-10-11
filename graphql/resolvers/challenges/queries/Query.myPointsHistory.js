import { util } from '@aws-appsync/utils'

const DEFAULT_PAGE_SIZE = 20
const MAX_PAGE_SIZE = 100

// AppSync direct DynamoDB resolver — Cognito only (ADR-0048 §1, ADR-0009): one Query on the
// points-transactions LSI1 (OwnerId + CreatedAt), newest first. Classified direct because it is a
// single-partition read by key with no domain rule; the balance is NOT summed from these rows
// (ADR-0048 §2) — it stays on myChallengeProgress.score.
//
// OwnerId always comes from the token: there is no client-supplied identity to trust, and guests
// have no ledger (they never score, ADR-0045 §7).
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  const { pageSize, nextToken } = ctx.args
  const req = {
    operation: 'Query',
    index: 'LSI1',
    query: {
      expression: 'OwnerId = :ownerId',
      expressionValues: util.dynamodb.toMapValues({ ':ownerId': `USER#${ctx.identity.sub}` }),
    },
    scanIndexForward: false,
    // Clamped so a client cannot ask for a zero/negative page or an oversized one; a Query is
    // capped at 1MB regardless, so this only bounds the work per request.
    limit: Math.max(1, Math.min(pageSize ?? DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE)),
  }
  if (nextToken) req.nextToken = nextToken
  return req
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  return {
    items: (ctx.result.items ?? []).map(item => ({
      id: item.TransactionId,
      type: item.Type,
      status: item.Status,
      points: item.Points,
      // The repository stores CreatedAt with "O" (7 fractional digits). AWSDateTime is only
      // guaranteed for millisecond precision, so trim to "YYYY-MM-DDTHH:mm:ss.SSSZ" — the
      // fixed-width prefix is also what keeps LSI1 ordering chronological.
      createdAt: item.CreatedAt.slice(0, 23) + 'Z',
      // Sparse attribute: only redemption rows carry it (ADR-0048 §1).
      orderId: item.OrderId ?? null,
    })),
    nextToken: ctx.result.nextToken ?? null,
  }
}
