# Inventory

Owns where stock is kept: the warehouses ("polos").
It is not a field on the product: the person who receives the goods (the **StockKeeper**) owns the
numbers, not the Seller who lists the product.

## Architecture

![Inventory architecture](../../../docs/diagrams/inventory.svg)

<sub>Source: [`docs/duckstore-process-flow.drawio`](../../../docs/duckstore-process-flow.drawio), page **Inventory**. Regenerate with `./scripts/export-diagrams.sh`.</sub>

## Responsibilities

- **Warehouses** — code, name, Brazilian address, priority and active flag
  ([ADR-0052](../../../docs/adr/0052-inventory-bounded-context-warehouses.md) §2).

**Codes are unique without a read.** `createWarehouse` writes the warehouse and a `CODE#<code>` guard
item in one `TransactWriteItems`, both `attribute_not_exists`, so a taken code cancels the whole
write (`WarehouseCodeTaken`). Codes are uppercase and immutable. Warehouses are deactivated, never
deleted, so balances and history can always point at them and guards are never removed.

**Priority is the allocation order.** Lower serves a sale first, ties broken by code, and it is the
extension point for a "nearest warehouse" policy. Today it orders the list.

**What it does not do.** It holds no stock balances or movements, the storefront still reads Catalog's
`Product.stock`, and no sale reserves stock — the direction for each is in ADR-0052 §6. There is no
`Inventory.Function` project: every field is a direct resolver.

## Data

| Table | Key | Stream | Notes |
|---|---|---|---|
| `inventory-warehouses` | PK `WarehouseId` | none | Two item types: `Type = Warehouse` (`<uuid>`) and `Type = CodeGuard` (`CODE#<code>`, `TargetWarehouseId`). Every read filters on `Type`. No GSI: tens of items, listed by a `Scan` |

## API surface (AppSync)

All five are direct DynamoDB resolvers ([ADR-0009](../../../docs/adr/0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md);
ADR-0052 §4 answers each escalation criterion). Each one checks the group on its first line: **StockKeeper
or Admin**, `Unauthorized` for anyone else, reads included.

| Field | Resolver |
|---|---|
| `warehouses` | `Scan` filtered on `Type = Warehouse`, sorted by priority then code |
| `warehouse(id)` | `GetItem`, `null` for an unknown id or a guard key |
| `createWarehouse(input)` | `TransactWriteItems` (warehouse + guard). `WarehouseCodeTaken`, or the retryable `TransactionConflict` when two creates race |
| `updateWarehouse(input)` | `UpdateItem` of name, address and priority (never the code). `NotFound` |
| `setWarehouseActive(id, active)` | `UpdateItem` of `Active`. `NotFound` |

Input errors are `BadRequest` with `errorInfo.field` (the GraphQL path, e.g. `address.zipCode`). The
rules live in the resolvers and are mirrored, with cross-referencing comments, by `local.ts`
(`lib/warehouse-validation.ts`) and the Blazor `WarehouseFormModel`.

The Blazor management app's **Warehouses** pages are the only client. The menu item appears for
StockKeeper and Admin.

## Integration events

None.

## Failure handling

No Lambda, stream or consumer, so there is no DLQ or alarm. A resolver failure is returned to the
caller as a GraphQL error.

## Local development

```bash
dotnet run --project src/AppHost/AppHost.csproj   # Inventory.DevelopmentDataSeeder creates the table and seeds ALPHA and BETA
node scripts/inventory/local-api-checks.mjs       # criteria checks against the SPA's local BFF (local.ts)
node scripts/inventory/evaluate-resolvers.mjs "$PWD"   # resolvers on the real APPSYNC_JS runtime (needs AWS credentials)
cd src/WebApps/Shopping.Web.SPA.React && pnpm test     # lib/warehouse-validation.test.ts
```

The deployed flow is covered by the Playwright suite `tests/E2E/Inventory/Inventory.E2ETests` (see
[`tests/E2E/README.md`](../../../tests/E2E/README.md)), signed in as a StockKeeper, an Admin and a Seller.

## Related ADRs

- [ADR-0052: Inventory Bounded Context](../../../docs/adr/0052-inventory-bounded-context-warehouses.md)
- [ADR-0009: AppSync Resolver Selection](../../../docs/adr/0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0050: Checkout as a Step Functions Express Saga](../../../docs/adr/0050-checkout-step-functions-express-saga.md) — where the stock reservation will live
