import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver (ADR-0009): admin-only DeleteItem by Id. A key delete needs
// no Lambda; DeleteItem is idempotent, so deleting a missing order still succeeds.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'DeleteItem',
    key: { Id: util.dynamodb.toDynamoDB(ctx.args.orderId) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: true }
}
