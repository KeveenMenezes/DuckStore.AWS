import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: GetItem on the coupons table by ProductName.
// Discount was merged into Basket; a key lookup needs no Lambda (ADR-0009).
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { ProductName: util.dynamodb.toDynamoDB(ctx.args.productName) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const r = ctx.result
  if (!r) {
    return { productName: ctx.args.productName, description: 'No Discount', amount: 0 }
  }

  return {
    productName: r.ProductName,
    description: r.Description,
    amount: r.Amount,
  }
}
