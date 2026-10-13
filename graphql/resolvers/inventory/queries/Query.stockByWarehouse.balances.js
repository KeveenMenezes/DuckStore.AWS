import { util } from '@aws-appsync/utils'

// Pipeline step: GSI1 of inventory-stock (PK WarehouseId), one page of the warehouse's balances.
export function request(ctx) {
  return {
    operation: 'Query',
    index: 'GSI1',
    query: {
      expression: 'WarehouseId = :warehouseId',
      expressionValues: util.dynamodb.toMapValues({ ':warehouseId': ctx.args.warehouseId }),
    },
    limit: ctx.args.pageSize ?? 20,
    nextToken: ctx.args.nextToken ?? null,
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    items: (ctx.result.items ?? []).map((item) => ({
      productId: item.ProductId,
      productName: null,
      available: item.Available,
      reserved: item.Reserved ?? 0,
    })),
    nextToken: ctx.result.nextToken ?? null,
  }
}
