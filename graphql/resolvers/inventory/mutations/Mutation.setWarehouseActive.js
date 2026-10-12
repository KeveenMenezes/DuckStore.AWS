import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052 §3). Direct UpdateItem resolver (ADR-0009). Warehouses are
// deactivated instead of deleted, so balances and the stock history keep pointing at a real item
// (ADR-0052 §2). What "inactive" stops (receipts, the storefront total, sales) arrives with
// SPEC-inventory.md modules 2-4; here it is only the flag.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'UpdateItem',
    key: util.dynamodb.toMapValues({ WarehouseId: ctx.args.id }),
    update: {
      expression: 'SET Active = :active, UpdatedAt = :now, UpdatedBy = :by',
      expressionValues: util.dynamodb.toMapValues({
        ':active': ctx.args.active,
        ':now': util.time.nowISO8601(),
        ':by': ctx.identity.sub,
      }),
    },
    // Without it, UpdateItem would upsert a phantom item for an unknown id, or flag a CODE# guard.
    condition: {
      expression: 'attribute_exists(WarehouseId) AND #type = :warehouse',
      expressionNames: { '#type': 'Type' },
      expressionValues: util.dynamodb.toMapValues({ ':warehouse': 'Warehouse' }),
    },
  }
}

// UpdateItem returns the full item after the write (ALL_NEW).
export function response(ctx) {
  if (ctx.error) {
    if (ctx.error.type === 'DynamoDB:ConditionalCheckFailedException') {
      util.error('Warehouse not found', 'NotFound')
    }
    util.error(ctx.error.message, ctx.error.type)
  }

  const item = ctx.result
  return {
    id: item.WarehouseId,
    code: item.Code,
    name: item.Name,
    address: {
      street: item.Street,
      number: item.Number,
      complement: item.Complement ?? null,
      district: item.District,
      city: item.City,
      state: item.State,
      zipCode: item.ZipCode,
    },
    priority: item.Priority,
    active: item.Active,
    createdAt: item.CreatedAt,
    updatedAt: item.UpdatedAt,
  }
}
