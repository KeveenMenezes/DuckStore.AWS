import { util } from '@aws-appsync/utils'

export function request(ctx) {
  // Ownership check: only the authenticated user can delete their own cart
  if (!ctx.identity || ctx.identity.username !== ctx.args.userName) util.unauthorized()
  return {
    operation: 'DeleteItem',
    key: { UserName: util.dynamodb.toDynamoDB(ctx.args.userName) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: true }
}
