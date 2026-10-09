import { util } from '@aws-appsync/utils'

// Pipeline function 2/2: UpdateItem on the Id function 1 computed — it transitions the existing
// row (Eligible, Published or Deleted) to Published and never creates one (ADR-0049 §4). The
// attribute_exists(Id) condition backs up function 1's check against a Deleted row's TTL removing
// it between the two functions. SET GSI1PK/GSI1SK puts the row into the sparse GSI1 (the only
// visibility mechanism for reviewsByProduct, ADR-0049 §2) and REMOVE ExpiresAt cancels a pending
// TTL on re-publish. CreatedAt is preserved from the existing row (the Eligible row already has it
// from purchase time) so GSI1SK — and reviewsByProduct's sort order — never moves on an edit;
// UpdatedAt is always refreshed. UserId is the Cognito sub (key component, immutable); UserName is
// display-only, read from the ID token's `name` claim at write time — never client-supplied, same
// pattern as Query.myProfile.js / Mutation.updateProfile.js. The status transition on the
// DynamoDB Streams MODIFY is what the publisher turns into ReviewCreated/ReviewUpdated (ADR-0049 §5).
export function request(ctx) {
  const { productId, rating, comment } = ctx.args.input
  const id = ctx.stash.id
  const existing = ctx.stash.existing
  const createdAt = existing?.CreatedAt ?? util.time.nowISO8601()
  const updatedAt = util.time.nowISO8601()

  return {
    operation: 'UpdateItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    update: {
      // Status and Comment are DynamoDB reserved words.
      expression:
        'SET #status = :published, Rating = :rating, #comment = :comment, UserName = :userName, ' +
        'UserId = :userId, CreatedAt = :createdAt, UpdatedAt = :updatedAt, ' +
        'GSI1PK = :gsi1pk, GSI1SK = :gsi1sk REMOVE ExpiresAt',
      expressionNames: { '#status': 'Status', '#comment': 'Comment' },
      expressionValues: util.dynamodb.toMapValues({
        ':published': 'Published',
        ':rating': rating,
        ':comment': comment,
        ':userName': ctx.identity.claims.name,
        ':userId': ctx.identity.sub,
        ':createdAt': createdAt,
        ':updatedAt': updatedAt,
        ':gsi1pk': productId,
        ':gsi1sk': createdAt,
      }),
    },
    condition: {
      expression: 'attribute_exists(Id)',
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.stash.id, userName: ctx.identity.claims.name }
}
