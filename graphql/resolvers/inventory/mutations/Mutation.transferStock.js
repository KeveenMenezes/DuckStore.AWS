import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Lambda resolver (ADR-0009 criteria 1 and 4, ADR-0052 §7):
// inventory-transfer-stock moves stock between two warehouses in one transaction. No product check,
// so a deleted product's remaining stock can still be moved out.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      IdempotencyKey: input.idempotencyKey,
      ProductId: input.productId,
      FromWarehouseId: input.fromWarehouseId,
      ToWarehouseId: input.toWarehouseId,
      Quantity: input.quantity,
      Note: input.note ?? null,
      Actor: ctx.identity.sub,
    },
  }
}

// The Lambda answers expected outcomes as data (ADR-0052 §7, the ADR-0050 §10 contract): a refusal
// becomes a typed GraphQL error with errorInfo { field, available }. Anything else — the Lambda threw
// or timed out — is InternalError with no detail, so stack traces never reach the client.
export function response(ctx) {
  if (ctx.error) util.error('The stock movement could not be recorded', 'InternalError')

  const result = ctx.result
  const refusal = result.Refusal
  if (refusal) {
    const info = {}
    if (refusal.Field) info.field = refusal.Field
    if (refusal.Available !== null && refusal.Available !== undefined) info.available = refusal.Available
    util.error(refusal.Message, refusal.ErrorType, null, info)
  }
  return { movements: result.Movements.map((movement) => toMovement(movement)) }
}

function toMovement(m) {
  return {
    id: m.Id,
    productId: m.ProductId,
    productName: null,
    type: m.Type,
    quantity: m.Quantity,
    balanceAfter: m.BalanceAfter,
    warehouseId: m.WarehouseId,
    warehouseCode: m.WarehouseCode,
    invoiceNumber: m.InvoiceNumber,
    supplier: m.Supplier,
    reason: m.Reason,
    note: m.Note,
    transferId: m.TransferId,
    counterpartWarehouseId: m.CounterpartWarehouseId,
    createdAt: m.CreatedAt,
    createdBy: m.CreatedBy,
  }
}
