import { util } from '@aws-appsync/utils'

// Pipeline step: invokes inventory-record-stock-receipt (ADR-0052 §7). The author is the Cognito sub
// from the token; nothing about who or when comes from the client.
export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      IdempotencyKey: input.idempotencyKey,
      ProductId: input.productId,
      WarehouseId: input.warehouseId,
      Quantity: input.quantity,
      InvoiceNumber: input.invoiceNumber,
      Supplier: input.supplier ?? null,
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
