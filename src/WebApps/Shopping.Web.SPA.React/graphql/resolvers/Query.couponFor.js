import { util } from '@aws-appsync/utils'

// Lambda resolver: invokes discount-get-discount directly.
export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { ProductName: ctx.args.productName },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    productName: r.ProductName,
    description: r.Description,
    amount: r.Amount,
  }
}
