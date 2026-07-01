import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  return {
    operation: 'DeleteItem',
    key: { Id: util.dynamodb.toDynamoDB(ctx.args.id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: true }
}
