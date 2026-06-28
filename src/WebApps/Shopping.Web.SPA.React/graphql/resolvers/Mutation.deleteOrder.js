import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { OrderId: ctx.args.orderId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: ctx.result.IsDeleted }
}
