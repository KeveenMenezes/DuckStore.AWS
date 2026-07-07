import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: GetItem on Pricing's "prices" table by ProductId (ADR-0026).
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { ProductId: util.dynamodb.toDynamoDB(ctx.args.productId) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

  const item = ctx.result
  return {
    productId: item.ProductId,
    nominalPrice: +item.NominalPrice,
    cost: +(item.Cost ?? 0),
    updatedAt: item.UpdatedAt,
  }
}
