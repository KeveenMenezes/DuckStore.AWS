import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: Scan Products table with optional pagination.
// AppSync pre-unmarshals items: item.Id is a plain string, item.Price a number string (N type),
// item.CategoryIds is a JS array (SS type).
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
    description: item.Description,
    imageUrl: item.ImageUrl,
    price: parseFloat(item.Price),
    stock: parseInt(item.Stock, 10),
    categoryIds: item.CategoryIds ?? [],
  }))

  return { items, nextToken: ctx.result.nextToken ?? null }
}
