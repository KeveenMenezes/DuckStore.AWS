import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver: GetItem from ShoppingCarts table.
// The Data attribute is a JSON blob; parse it in the response handler.
// NOTE: In production, replace with a Lambda resolver if Redis cache-aside is required.
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { UserName: util.dynamodb.toDynamoDB(ctx.args.userName) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

  // Data is a JSON string serialized by the .NET Lambda.
  // Inspect actual DynamoDB records to confirm property casing (camelCase vs PascalCase).
  const cart = JSON.parse(ctx.result.Data)

  return {
    userName: ctx.result.UserName,
    items: (cart.Items ?? cart.items ?? []).map(i => ({
      quantity: i.Quantity ?? i.quantity,
      color: i.Color ?? i.color ?? null,
      price: i.Price ?? i.price,
      productId: i.ProductId ?? i.productId,
      productName: i.ProductName ?? i.productName,
    })),
    totalPrice: cart.TotalPrice ?? cart.totalPrice ?? 0,
  }
}
