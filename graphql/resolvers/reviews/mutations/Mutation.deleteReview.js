import { util } from '@aws-appsync/utils'

// Cognito-only. Direct DynamoDB UpdateItem (ADR-0009) on the caller's own row,
// `${productId}#${ctx.identity.sub}` (ADR-0037) — the sub is derived server-side, never taken
// from input. Transitions Published -> Deleted (ADR-0049 §4): REMOVE GSI1PK/GSI1SK drops the row
// out of the sparse GSI1 so reviewsByProduct stops returning it (ADR-0049 §2), and ExpiresAt
// (epoch seconds) lets the table's TTL remove it after 5 days — until then the customer can
// re-publish via createReview. The condition only accepts an existing row that is Published or
// legacy without Status (= Published); a missing row or an Eligible/Deleted one fails with
// ConditionalCheckFailedException (UpdateItem must never create a row here). The publisher emits
// ReviewDeletedEvent off this MODIFY; the later TTL REMOVE emits nothing (ADR-0049 §5).
const TTL_SECONDS = 5 * 24 * 60 * 60

export function request(ctx) {
  if (!ctx.identity?.sub) util.unauthorized()

  const id = `${ctx.args.productId}#${ctx.identity.sub}`
  return {
    operation: 'UpdateItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    update: {
      // Status is a DynamoDB reserved word.
      expression: 'SET #status = :deleted, ExpiresAt = :expiresAt, UpdatedAt = :now REMOVE GSI1PK, GSI1SK',
      expressionNames: { '#status': 'Status' },
      expressionValues: util.dynamodb.toMapValues({
        ':deleted': 'Deleted',
        ':expiresAt': util.time.nowEpochSeconds() + TTL_SECONDS,
        ':now': util.time.nowISO8601(),
      }),
    },
    condition: {
      // attribute_exists(Id) first: attribute_not_exists(Status) is also true on a missing item,
      // so without it UpdateItem would create a Deleted row for a product never bought.
      // Separate placeholder from the update's #status, so the two name maps never collide when
      // AppSync merges them into one request.
      expression: 'attribute_exists(Id) AND (#condStatus = :published OR attribute_not_exists(#condStatus))',
      expressionNames: { '#condStatus': 'Status' },
      expressionValues: util.dynamodb.toMapValues({ ':published': 'Published' }),
    },
  }
}

// UpdateItem returns the item as it is after the update, mapped like Query.myReview.js.
export function response(ctx) {
  if (ctx.error) {
    // Same message as local.ts, instead of DynamoDB's raw "The conditional request failed".
    if (ctx.error.type === 'DynamoDB:ConditionalCheckFailedException') {
      util.error('No published review to delete', ctx.error.type)
    }
    util.error(ctx.error.message, ctx.error.type)
  }

  const item = ctx.result
  return {
    id: item.Id,
    productId: item.ProductId,
    userName: item.UserName ?? '',
    rating: Math.floor(+(item.Rating ?? 0)),
    comment: item.Comment ?? '',
    createdAt: item.CreatedAt,
    updatedAt: item.UpdatedAt ?? item.CreatedAt,
    status: item.Status ?? 'Published',
  }
}
