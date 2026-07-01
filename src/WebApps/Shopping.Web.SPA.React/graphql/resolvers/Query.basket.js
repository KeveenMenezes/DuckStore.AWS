import { util } from '@aws-appsync/utils'

export function request(ctx) {
  // Ownership check: only the authenticated user can read their own cart
  if (!ctx.identity || ctx.identity.username !== ctx.args.userName) util.unauthorized()
  return {
    operation: 'GetItem',
    key: { UserName: util.dynamodb.toDynamoDB(ctx.args.userName) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

  // The basket is stored as a JSON blob in the Data attribute (PascalCase from .NET serializer).
  const cart = JSON.parse(ctx.result.Data)
  return {
    userName: cart.UserName,
    items: (cart.Items ?? []).map(i => ({
      quantity: i.Quantity,
      color: i.Color ?? null,
      price: i.Price,
      productId: `${i.ProductId}`,
      productName: i.ProductName,
    })),
    totalPrice: cart.TotalPrice,
  }
}
