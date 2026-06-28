import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      Cart: {
        UserName: input.userName,
        Items: input.items.map(i => ({
          Quantity: i.quantity,
          Color: i.color ?? '',
          Price: i.price,
          ProductId: i.productId,
          ProductName: i.productName,
        })),
      },
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { userName: ctx.result.UserName }
}
