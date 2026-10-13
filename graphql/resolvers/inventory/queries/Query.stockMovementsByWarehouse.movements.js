import { util } from '@aws-appsync/utils'

// Pipeline step: GSI1 of inventory-movements (PK WarehouseId, SK CreatedAt), newest first.
export function request(ctx) {
  return {
    operation: 'Query',
    index: 'GSI1',
    query: {
      expression: 'WarehouseId = :warehouseId',
      expressionValues: util.dynamodb.toMapValues({ ':warehouseId': ctx.args.warehouseId }),
    },
    scanIndexForward: false,
    limit: ctx.args.pageSize ?? 20,
    nextToken: ctx.args.nextToken ?? null,
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    items: (ctx.result.items ?? []).map((item) => toMovement(item)),
    nextToken: ctx.result.nextToken ?? null,
  }
}

function toMovement(item) {
  return {
    id: item.MovementId,
    productId: item.ProductId,
    productName: null,
    type: item.Type,
    quantity: item.Quantity,
    balanceAfter: item.BalanceAfter,
    warehouseId: item.WarehouseId,
    warehouseCode: item.WarehouseCode,
    invoiceNumber: item.InvoiceNumber ?? null,
    supplier: item.Supplier ?? null,
    reason: item.Reason ?? null,
    note: item.Note ?? null,
    transferId: item.TransferId ?? null,
    counterpartWarehouseId: item.CounterpartWarehouseId ?? null,
    createdAt: item.CreatedAt,
    createdBy: item.CreatedBy,
  }
}
