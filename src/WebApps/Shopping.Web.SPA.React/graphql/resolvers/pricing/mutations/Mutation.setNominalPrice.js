import { util } from '@aws-appsync/utils'

// Admin/Seller only. Direct DynamoDB UpdateItem resolver (ADR-0009) — decoupled from
// createProduct/updateProduct (ADR-0026): Catalog no longer stores price. The `prices` item only
// ever persists a single `UpdatedAt` timestamp (no separate CreatedAt is stored — see
// DynamoPriceRepository.ToItem, now removed), so a plain UpdateItem upsert is a faithful
// replacement for the old get-or-create Lambda flow, no prior read needed.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  return {
    operation: 'UpdateItem',
    key: { ProductId: util.dynamodb.toDynamoDB(ctx.args.productId) },
    update: {
      expression: 'SET NominalPrice = :nominalPrice, Cost = :cost, UpdatedAt = :now',
      expressionValues: util.dynamodb.toMapValues({
        ':nominalPrice': ctx.args.price,
        ':cost': ctx.args.cost,
        ':now': util.time.nowISO8601(),
      }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const r = ctx.result
  return {
    productId: r.ProductId,
    nominalPrice: r.NominalPrice,
    cost: r.Cost,
    updatedAt: r.UpdatedAt,
  }
}
