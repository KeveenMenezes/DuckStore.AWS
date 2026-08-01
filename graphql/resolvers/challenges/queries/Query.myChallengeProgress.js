import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver — Cognito only; a single Query on OwnerId returns PROFILE and
// every ATTEMPT row in one round trip (ADR-0045 §4/§5). OwnerId always comes from the token —
// guests play but never score (ADR-0045 §7), so there is no client-supplied identity to trust.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'Query',
    query: {
      expression: 'OwnerId = :ownerId',
      expressionValues: util.dynamodb.toMapValues({ ':ownerId': `USER#${ctx.identity.sub}` }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const items = ctx.result.items ?? []
  const profile = items.find(item => item.SK === 'PROFILE')
  // An ATTEMPT row with no IsCorrect yet is a hint-only placeholder (a hint was revealed before
  // the question was answered, ADR-0045 §6) — not a completed attempt.
  const attemptItems = items.filter(item => item.SK && item.SK.startsWith('ATTEMPT#') && item.IsCorrect !== undefined)

  const byLanguage = {}
  if (profile) {
    for (const key of Object.keys(profile)) {
      if (key.startsWith('Lang#')) byLanguage[key.slice(5)] = profile[key]
    }
  }

  return {
    score: profile?.Score ?? 0,
    completed: profile?.Completed ?? 0,
    correctCount: profile?.CorrectCount ?? 0,
    wrongCount: profile?.WrongCount ?? 0,
    hintsUsed: profile?.HintsUsed ?? 0,
    currentStreak: profile?.CurrentStreak ?? 0,
    lastAnsweredAt: profile?.LastAnsweredAt ?? null,
    byLanguage,
    attempts: attemptItems.map(item => ({
      questionId: item.SK.slice(8),
      isCorrect: Boolean(item.IsCorrect),
      selectedOption: item.SelectedOption ?? 0,
      hintsRevealed: item.HintsRevealed ?? 0,
      pointsEarned: item.PointsEarned ?? 0,
      answeredAt: item.AnsweredAt ?? null,
    })),
  }
}
