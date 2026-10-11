import { util } from '@aws-appsync/utils'

// Pipeline function 1/2: GetItem on the composite Id `${productId}#${ctx.identity.sub}` — one row
// per (productId, authenticated user). The Cognito sub is derived server-side, never taken from
// client input, so a client can't write another user's review (ADR-0037). The row is the purchase
// gate (ADR-0049 §4): review-order-completed-consumer creates it as Eligible when an order
// completes, so no row means the customer never bought the product (or a Deleted row's TTL
// expired) and the mutation is rejected here. Stashes the computed id and the existing item so
// function 2 can preserve CreatedAt.
export function request(ctx) {
  if (!ctx.identity?.sub) util.unauthorized()

  const { productId } = ctx.args.input
  const id = `${productId}#${ctx.identity.sub}`
  ctx.stash.id = id
  return {
    operation: 'GetItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) util.unauthorized()
  ctx.stash.existing = ctx.result
  return ctx.result
}
