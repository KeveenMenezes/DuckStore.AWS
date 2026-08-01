import { util } from '@aws-appsync/utils'

// AppSync Mutation resolver, Lambda-backed (ADR-0009/ADR-0045 §6, §8): the hint penalty is a
// conditional write that must commit before the hint text is returned — a domain invariant
// (no hint after answering, cap at HintCount, penalty arithmetic) that belongs in Question, not a
// resolver script.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: {
      OwnerId: `USER#${ctx.identity.sub}`,
      ChallengeId: ctx.args.challengeId,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    hint: r.Hint,
    hintsRevealed: r.HintsRevealed,
    penaltyApplied: r.PenaltyApplied,
  }
}
