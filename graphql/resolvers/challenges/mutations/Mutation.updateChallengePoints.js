import { util } from '@aws-appsync/utils'

// Admin only (Seller deliberately excluded). Direct DynamoDB UpdateItem resolver (ADR-0009): one
// item, one attribute, and the only invariant (points > 0) fits in the resolver. Only the PUBLIC
// item changes — Question.Grade reads Points from it at answer time, so the new value applies to
// future answers without touching past attempts or the points ledger. The ANSWER item is never
// addressed here (ADR-0045 §2).
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  const { id, points } = ctx.args
  // GraphQL Int already rejects non-integers; this keeps the invariant local if the arg type ever loosens.
  if (!Number.isInteger(points) || points <= 0) {
    util.error('points must be a positive integer', 'BadRequest')
  }

  return {
    operation: 'UpdateItem',
    key: {
      QuestionId: util.dynamodb.toDynamoDB(id),
      SK: util.dynamodb.toDynamoDB('PUBLIC'),
    },
    update: {
      expression: 'SET Points = :points, UpdatedAt = :now',
      expressionValues: util.dynamodb.toMapValues({
        ':points': points,
        ':now': util.time.nowISO8601(),
      }),
    },
    // Without this, UpdateItem would upsert a phantom PUBLIC item for an unknown id.
    condition: { expression: 'attribute_exists(QuestionId)' },
  }
}

// UpdateItem returns the full item after the write (ALL_NEW); same field mapping as Query.challenge.
export function response(ctx) {
  if (ctx.error) {
    if (ctx.error.type === 'DynamoDB:ConditionalCheckFailedException') {
      util.error('Challenge not found', 'NOT_FOUND')
    }
    util.error(ctx.error.message, ctx.error.type)
  }

  const item = ctx.result
  return {
    id: item.QuestionId,
    title: item.Title,
    description: item.Description,
    code: item.Code,
    options: item.Options ?? [],
    difficulty: (item.Difficulty ?? '').toUpperCase(),
    language: item.Language,
    points: item.Points ?? 0,
    hintCount: item.HintCount ?? 0,
  }
}
