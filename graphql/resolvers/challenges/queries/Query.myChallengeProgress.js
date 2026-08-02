import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver — Cognito only; a single Query on OwnerId returns PROFILE and
// every ATTEMPT row in one round trip (ADR-0045 §4/§5). OwnerId always comes from the token —
// guests play but never score (ADR-0045 §7), so there is no client-supplied identity to trust.
//
// Deliberately unpaginated, and that is the ceiling on this field: a Query returns at most 1MB and
// a direct resolver cannot follow LastEvaluatedKey, so a player whose partition outgrows 1MB would
// silently lose the tail of their attempts. Attempt rows are tiny (~150 bytes), which puts the
// ceiling in the thousands of answered questions — well past the size of the question bank. If the
// bank ever approaches it, this field is the escalation ADR-0009 describes: move to a Lambda
// resolver that pages, or add a paginated `attempts` field. Do not "fix" it by adding a `limit`
// here — that truncates sooner, and silently.
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
  // Array.prototype.find isn't in the AppSync JS runtime's allow-listed array methods (only
  // map/filter/forEach/reduce accept a function argument, per Query.products.js) — filter()[0]
  // is the supported equivalent.
  const profileItems = items.filter(item => item.SK === 'PROFILE')
  const profile = profileItems.length > 0 ? profileItems[0] : null
  // An ATTEMPT row with no IsCorrect yet is a hint-only placeholder (a hint was revealed before
  // the question was answered, ADR-0045 §6) — not a completed attempt.
  const attemptItems = items.filter(item => item.SK && item.SK.startsWith('ATTEMPT#') && item.IsCorrect !== undefined)

  const byLanguage = {}
  if (profile) {
    // No for...of here — the AppSync JS runtime rejects classic loop constructs the same way it
    // rejects for(;;)/while; Object.keys().forEach() stays within the allow-listed array methods.
    Object.keys(profile).forEach(key => {
      if (key.startsWith('Lang#')) byLanguage[key.slice(5)] = profile[key]
    })
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
      // Boolean(...) isn't in the AppSync JS runtime's supported primitives (no Boolean section
      // in its docs, unlike Object/String/Number/Math/Array) — strict equality is the safe coercion.
      isCorrect: item.IsCorrect === true,
      selectedOption: item.SelectedOption ?? 0,
      hintsRevealed: item.HintsRevealed ?? 0,
      pointsEarned: item.PointsEarned ?? 0,
      answeredAt: item.AnsweredAt ?? null,
    })),
  }
}
