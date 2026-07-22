import { util } from '@aws-appsync/utils'

// Pipeline function 2/2: PutItem on the Id function 1 computed. A resubmission by the same
// authenticated customer for the same product overwrites the row (upsert) instead of creating a
// duplicate — CreatedAt is preserved from the existing item so GSI1SK (and therefore
// reviewsByProduct's sort order) never moves on an edit; UpdatedAt is always refreshed. UserId is
// the Cognito sub (key component, immutable); UserName is display-only, read from the ID token's
// `name` claim at write time — never client-supplied, same pattern as Query.myProfile.js /
// Mutation.updateProfile.js. The DynamoDB Streams MODIFY this produces (vs. INSERT for a
// first-time review) is what drives the CDC rating-delta flow in CatalogView
// (ReviewUpdatedRule/ReviewUpdatedEvent).
export function request(ctx) {
  const { productId, rating, comment } = ctx.args.input
  const id = ctx.stash.id
  const existing = ctx.stash.existing
  const createdAt = existing?.CreatedAt ?? util.time.nowISO8601()
  const updatedAt = util.time.nowISO8601()

  return {
    operation: 'PutItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    attributeValues: {
      ProductId: util.dynamodb.toDynamoDB(productId),
      UserId: util.dynamodb.toDynamoDB(ctx.identity.sub),
      UserName: util.dynamodb.toDynamoDB(ctx.identity.claims.name),
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
  return { id: ctx.stash.id, userName: ctx.identity.claims.name }
}
