import { util } from '@aws-appsync/utils'

// Top-level pipeline resolver (ADR-0029) — the first pipeline resolver in this repo. Both steps
// are DynamoDB operations on the same table/aggregate (GetItem to recover CreatedAt, then PutItem
// as an upsert keyed by productId+userName), so this stays a Direct classification per ADR-0009:
// no Lambda, no EventBridge publish, no cross-aggregate transaction. See
// Mutation.createReview.checkExisting.js and Mutation.createReview.upsert.js for the two functions.
export function request(ctx) {
  return {}
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.prev.result
}
