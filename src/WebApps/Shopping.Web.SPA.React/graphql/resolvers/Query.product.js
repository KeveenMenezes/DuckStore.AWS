import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: GetItem by Id.
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { Id: util.dynamodb.toDynamoDB(ctx.args.id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

  const item = ctx.result
  return {
    id: item.Id,
    name: item.Name,
    description: item.Description,
    imageUrl: item.ImageUrl,
    price: parseFloat(item.Price),
    stock: parseInt(item.Stock, 10),
    categoryIds: item.CategoryIds ?? [],
  }
}
