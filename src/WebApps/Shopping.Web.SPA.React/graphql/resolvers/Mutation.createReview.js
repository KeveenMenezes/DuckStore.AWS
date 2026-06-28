import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: PutItem on Reviews. GSI1 (GSI1PK=ProductId, GSI1SK=CreatedAt)
// backs reviewsByProduct. The insert drives the ReviewCreated CDC flow that updates the
// product's rating (ADR-0011) — the resolver itself does NOT publish any event.
export function request(ctx) {
  const { productId, userName, rating, comment } = ctx.args.input
  const id = util.autoId()
  const createdAt = util.time.nowISO8601()
  ctx.stash.id = id
  return {
    operation: 'PutItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    attributeValues: {
      ProductId: util.dynamodb.toDynamoDB(productId),
      UserName: util.dynamodb.toDynamoDB(userName),
      Rating: util.dynamodb.toDynamoDB(rating),
      Comment: util.dynamodb.toDynamoDB(comment),
      CreatedAt: util.dynamodb.toDynamoDB(createdAt),
      GSI1PK: util.dynamodb.toDynamoDB(productId),
      GSI1SK: util.dynamodb.toDynamoDB(createdAt),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.stash.id }
}
