import { util } from '@aws-appsync/utils'

// AppSync Mutation resolver, Lambda-backed (ADR-0009/ADR-0045 §8): grading needs the stored
// answer key and finalizes through a multi-item transaction, both beyond a direct resolver.
// OwnerId always comes from the Cognito token — the client only ever sends challengeId and
// selectedOption; accepting points/isCorrect/hintsUsed from the client is NOT ALLOWED (ADR-0045 §3).
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: {
      OwnerId: `USER#${ctx.identity.sub}`,
      ChallengeId: ctx.args.challengeId,
      SelectedOption: ctx.args.selectedOption,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    isCorrect: r.IsCorrect,
    pointsEarned: r.PointsEarned,
    newScore: r.NewScore,
    explanation: r.Explanation,
    // The option the stored attempt was graded on — on a re-submission that is the first answer,
    // not ctx.args.selectedOption (ADR-0045 §4).
    selectedOption: r.SelectedOption,
  }
}
