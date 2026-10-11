import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: Query the Reviews GSI1 by ProductId, newest-first.
export function request(ctx) {
  const limit = ctx.args.pageSize ?? 20
  const req = {
    operation: 'Query',
    index: 'GSI1',
    query: {
      expression: 'GSI1PK = :pk',
      expressionValues: util.dynamodb.toMapValues({ ':pk': ctx.args.productId }),
    },
    scanIndexForward: false,
    limit,
  }
  if (ctx.args.nextToken) req.nextToken = ctx.args.nextToken
  return req
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const items = (ctx.result.items ?? []).map(item => ({
    id: item.Id,
    productId: item.ProductId,
    userName: item.UserName,
    rating: Math.floor(+item.Rating),
    comment: item.Comment,
    createdAt: item.CreatedAt,
    updatedAt: item.UpdatedAt,
    // GSI1 is sparse — only Published rows are indexed, so no status filter here (ADR-0049 §2).
    // Legacy rows written before Status existed are Published.
    status: item.Status ?? 'Published',
  }))

  return { items, nextToken: ctx.result.nextToken ?? null }
}
