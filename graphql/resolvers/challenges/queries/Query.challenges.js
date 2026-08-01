import { util } from '@aws-appsync/utils'

const DIFFICULTY_TO_STORED = { EASY: 'Easy', MEDIUM: 'Medium', HARD: 'Hard' }

// AppSync direct DynamoDB resolver on the "challenges" table (ADR-0045 §2/§8). GSI1 is sparse —
// only the PUBLIC item of each question carries GSI1PK/GSI1SK — so neither branch below can
// surface the ANSWER item. The no-language branch Scans that same index rather than the base
// table: a base-table Scan would need an SK = PUBLIC filter, and DynamoDB applies `limit` to items
// *read* before filtering, so every page would come back roughly half the requested size (each
// question is two items). Scanning the sparse index makes `limit` mean what the caller asked for.
export function request(ctx) {
  const { language, difficulty, pageSize, nextToken } = ctx.args
  const limit = pageSize ?? 20
  const storedDifficulty = difficulty ? DIFFICULTY_TO_STORED[difficulty] : null

  if (language) {
    const req = {
      operation: 'Query',
      index: 'GSI1',
      query: {
        expression: storedDifficulty
          ? 'GSI1PK = :pk AND begins_with(GSI1SK, :difficultyPrefix)'
          : 'GSI1PK = :pk',
        expressionValues: storedDifficulty
          ? util.dynamodb.toMapValues({ ':pk': language, ':difficultyPrefix': `${storedDifficulty}#` })
          : util.dynamodb.toMapValues({ ':pk': language }),
      },
      limit,
    }
    if (nextToken) req.nextToken = nextToken
    return req
  }

  const req = {
    operation: 'Scan',
    index: 'GSI1',
    limit,
  }
  if (storedDifficulty) {
    req.filter = {
      expression: 'Difficulty = :difficulty',
      expressionValues: util.dynamodb.toMapValues({ ':difficulty': storedDifficulty }),
    }
  }
  if (nextToken) req.nextToken = nextToken
  return req
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const items = (ctx.result.items ?? []).map(item => ({
    id: item.QuestionId,
    title: item.Title,
    description: item.Description,
    code: item.Code,
    options: item.Options ?? [],
    difficulty: (item.Difficulty ?? '').toUpperCase(),
    language: item.Language,
    points: item.Points ?? 0,
    hintCount: item.HintCount ?? 0,
  }))

  return { items, nextToken: ctx.result.nextToken ?? null }
}
