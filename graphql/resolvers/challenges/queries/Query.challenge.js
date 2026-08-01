import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver — GetItem with an explicit SK=PUBLIC key (ADR-0045 §2). The
// table's primary key is composite (QuestionId, SK), so a GetItem can never omit SK, and PUBLIC is
// the only sort key value this resolver is allowed to ask for.
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: {
      QuestionId: util.dynamodb.toDynamoDB(ctx.args.id),
      SK: util.dynamodb.toDynamoDB('PUBLIC'),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

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
