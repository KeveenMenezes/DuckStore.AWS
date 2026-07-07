import { util } from '@aws-appsync/utils'

// Pipeline function 2/2 (ADR-0029): PutItem on the same composite Id function 1 computed. A repeat
// submission by the same customer for the same product overwrites the existing row (upsert) instead
// of creating a duplicate — CreatedAt is preserved from the existing item so GSI1SK (and therefore
// reviewsByProduct's sort order) never moves on an edit; UpdatedAt is always refreshed. The
// DynamoDB Streams MODIFY this produces (vs. INSERT for a first-time review) is what drives the
// CDC rating-delta flow in CatalogView (ReviewUpdatedRule/ReviewUpdatedEvent).
export function request(ctx) {
  const { productId, userName, rating, comment } = ctx.args.input
  const id = ctx.stash.id
  const existing = ctx.stash.existing
  const createdAt = existing?.CreatedAt ?? util.time.nowISO8601()
  const updatedAt = util.time.nowISO8601()

  return {
    operation: 'PutItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    attributeValues: {
      ProductId: util.dynamodb.toDynamoDB(productId),
      UserName: util.dynamodb.toDynamoDB(userName),
      Rating: util.dynamodb.toDynamoDB(rating),
      Comment: util.dynamodb.toDynamoDB(comment),
      CreatedAt: util.dynamodb.toDynamoDB(createdAt),
      UpdatedAt: util.dynamodb.toDynamoDB(updatedAt),
      GSI1PK: util.dynamodb.toDynamoDB(productId),
      GSI1SK: util.dynamodb.toDynamoDB(createdAt),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.stash.id }
}
