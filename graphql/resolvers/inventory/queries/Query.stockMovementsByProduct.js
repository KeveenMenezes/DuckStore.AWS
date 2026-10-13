import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct Query on LSI1 of inventory-movements (ADR-0052 §7): the
// product's history across every warehouse, newest first.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'Query',
    index: 'LSI1',
    query: {
      expression: 'ProductId = :productId',
      expressionValues: util.dynamodb.toMapValues({ ':productId': ctx.args.productId }),
    },
    scanIndexForward: false,
    limit: pageSizeOf(ctx),
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

// pageSize 1-100 (default 20): a page never exceeds BatchGetItem's 100 keys (ADR-0052 §7).
function pageSizeOf(ctx) {
  const size = ctx.args.pageSize ?? 20
  if (Math.floor(size) !== size || size < 1 || size > 100) {
    util.error('pageSize must be an integer between 1 and 100', 'BadRequest', null, { field: 'pageSize' })
  }
  return size
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
