import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  const { name, description, imageUrl, stock, categoryIds } = ctx.args.input
  const id = util.autoId()
  ctx.stash.id = id
  // Price is set separately via Pricing's setNominalPrice mutation (ADR-0026) — Catalog no
  // longer stores it.
  return {
    operation: 'PutItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    attributeValues: {
      Name: util.dynamodb.toDynamoDB(name),
      Description: util.dynamodb.toDynamoDB(description),
      ImageUrl: util.dynamodb.toDynamoDB(imageUrl),
      Stock: util.dynamodb.toDynamoDB(stock),
      CategoryIds: util.dynamodb.toStringSet(categoryIds),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.stash.id }
}
