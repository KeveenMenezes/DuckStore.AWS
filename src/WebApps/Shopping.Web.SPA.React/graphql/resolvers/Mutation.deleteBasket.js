import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'DeleteItem',
    key: { UserName: util.dynamodb.toDynamoDB(ctx.args.userName) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: true }
}
