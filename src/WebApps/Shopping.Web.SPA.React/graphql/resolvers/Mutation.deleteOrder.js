import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: { OrderId: ctx.args.orderId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: ctx.result.IsDeleted }
}
