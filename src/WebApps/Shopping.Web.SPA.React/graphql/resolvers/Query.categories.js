import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: Scan Categories table with optional pagination.
export function request(ctx) {
  const limit = ctx.args.pageSize ?? 20
  const req = { operation: 'Scan', limit }
  if (ctx.args.nextToken) req.nextToken = ctx.args.nextToken
  return req
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const items = (ctx.result.items ?? []).map(item => ({
    id: item.Id,
    name: item.Name,
    parentId: item.ParentId ?? null,
    path: item.Path ?? [],
  }))

  return { items, nextToken: ctx.result.nextToken ?? null }
}
