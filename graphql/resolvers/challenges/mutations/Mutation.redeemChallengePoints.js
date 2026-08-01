import { util } from '@aws-appsync/utils'

// AppSync Mutation resolver, Lambda-backed (ADR-0009/ADR-0046 §2): the balance debit is a
// conditional TransactWriteItems (Score >= points, enforced by the database) — a domain invariant
// that belongs in PlayerProgress, not a resolver script. OwnerId always comes from the Cognito
// token; the client only ever sends a point quantity, never a currency amount (ADR-0046 §1).
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: {
      OwnerId: `USER#${ctx.identity.sub}`,
      Points: ctx.args.points,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    redemptionId: r.RedemptionId,
    newBalance: r.NewBalance,
  }
}
