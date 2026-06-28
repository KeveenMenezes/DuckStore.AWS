import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const { name, description, imageUrl, price, stock, categoryIds } = ctx.args.input
  const id = util.autoId()
  ctx.stash.id = id
  return {
    operation: 'PutItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    attributeValues: {
      Name: util.dynamodb.toDynamoDB(name),
      Description: util.dynamodb.toDynamoDB(description),
      ImageUrl: util.dynamodb.toDynamoDB(imageUrl),
      Price: util.dynamodb.toDynamoDB(price),
      Stock: util.dynamodb.toDynamoDB(stock),
      CategoryIds: util.dynamodb.toStringSet(categoryIds),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.stash.id }
}
