---
tags:
  - status/proposed
  - domain/inventory
  - domain/catalog
  - domain/appsync
---

# ADR-0052: Inventory Bounded Context — Stock per Warehouse, Owned by the StockKeeper

## Status
**Proposed** — October 2026

Specified by [SPEC-inventory-warehouses.md](../../SPEC-inventory-warehouses.md), module 1 of
[SPEC-inventory.md](../../SPEC-inventory.md). The later modules of that map amend this record
(§6). ADR-0051 is reserved for the order orchestration ([SPEC-checkout.md](../../SPEC-checkout.md) §5),
which is why this one is 0052.

---

## Context

Stock today is a single integer, `Product.Stock`, on Catalog's `products` table:

- **The wrong person owns it.** The Seller types it in the Blazor product form (`ProductForm.razor`),
  and it travels in `createProduct`/`updateProduct`/`createProductWithPrice`. Whoever receives the
  goods has no say in it.
- **No location.** The business keeps the same product in several warehouses ("polos"): product A
  can have 20 units in Alpha and 50 in Beta. One integer cannot say where anything is.
- **No movements.** Nothing records goods coming in or going out, and nothing decrements stock on a
  sale: checkout, ordering and payment all ignore `Stock`. The number is whatever someone last typed.
- **No role for it.** The management pool has only `Admin` and `Seller`.

Stock has its own lifecycle (receipt, adjustment, transfer, reservation by a sale), its own
operators and its own events. That makes it a bounded context, not a field on the product.

---

## Decision

### 1. Inventory owns stock per product and warehouse

A new **Inventory** bounded context owns how many units of each product exist in each warehouse.
Catalog stops owning stock: `Product.Stock` and the `stock` inputs leave Catalog when the storefront
starts reading Inventory's total (module `catalog-stock-handoff`, §6). Until then, the two coexist
and nothing reads Inventory from the storefront.

Inventory follows the reference structure ([ADR-0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)):
its own DynamoDB tables, CDK stack (`DuckStoreInventoryStack`) and `src/Services/Inventory/`. Its
`.NET Inventory.Function` project is created only when the first Lambda appears (module
`inventory-movements`), with one module per aggregate: `Modules/Warehouses` and `Modules/Stock`.
Module 1 is entirely direct resolvers.

### 2. The warehouse

| Attribute | Rule |
|---|---|
| `WarehouseId` | Internal id (UUID). Everything else in Inventory (balances, ledger) references it |
| `Code` | Human label (`ALPHA`, `SP-01`): `^[A-Z0-9][A-Z0-9-]{1,19}$`, normalized to uppercase, **unique** and **immutable** |
| `Name`, address | Name plus a Brazilian address (street, number, complement, district, city, UF, 8-digit CEP). No coordinates, no internal location (aisle/shelf) |
| `Priority` | Integer 1–999. Lower serves a sale first; ties are broken by `Code`. It is the extension point for a future "nearest warehouse" allocation policy |
| `Active` | Warehouses are **deactivated, never deleted**, so the history keeps every reference valid |

Table `inventory-warehouses`, PK `WarehouseId`:

```text
WarehouseId = <uuid>          Type = Warehouse   Code, Name, address…, Priority, Active, CreatedAt, UpdatedAt, UpdatedBy
WarehouseId = CODE#<code>     Type = CodeGuard   TargetWarehouseId
```

**Uniqueness is structural.** `createWarehouse` writes the warehouse and its `CODE#` guard in one
`TransactWriteItems`, both conditioned on `attribute_not_exists(WarehouseId)`. A taken code cancels
the whole write. There is no read-before-write, so two concurrent creates of one code can never
both succeed: the loser is cancelled either by the guard's condition (`ConditionalCheckFailed` →
`WarehouseCodeTaken`) or, when both transactions hit the guard at the same instant, by DynamoDB's
own `TransactionConflict` (→ the retryable `TransactionConflict` error; the retry then sees the
guard and fails as `WarehouseCodeTaken`). Guards are
never deleted, because codes never change and warehouses are never deleted. Every read of the
table MUST filter on `Type = Warehouse`, or it would return guards as warehouses.

### 3. Who reads and writes

A new Cognito group, **`StockKeeper`**, in the management pool (`duckstore-management-users`),
provisioned like the other staff groups (`CfnUserPoolGroup`, users added with AdminCreateUser).

| Group | Warehouses (read) | Warehouses (write) | Stock movements (module 2) | Stock total (module 3) |
|---|---|---|---|---|
| `StockKeeper` | Yes | Yes | Yes | Yes |
| `Admin` | Yes | Yes | Yes | Yes |
| `Seller` | No | No | No | Read only |
| Customer (shopping pool) | No | No | No | Through the storefront product only |

Every Inventory resolver MUST check the group on its first line and call `util.unauthorized()`
otherwise. The Blazor app hides what a group cannot do, but the resolver is the authority.

### 4. Resolvers

All five warehouse fields are **direct** resolvers ([ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)).
`createWarehouse` is the repository's first **direct** `TransactWriteItems` (the existing ones,
`createCampaign`/`endCampaign`, are Lambdas because they span two tables), so each criterion is
answered explicitly:

| Criterion | Why it does not apply |
|---|---|
| 1. Complex business validation | Every rule is a check on the input. Uniqueness is enforced by the conditional write, not by reading first — the ADR-0009 §5 idiom |
| 2. Multi-service orchestration | No other service is called |
| 3. External integration | DynamoDB only |
| 4. Cross-aggregate transaction | The `CODE#` guard is not an aggregate root: it is the Warehouse's own uniqueness index, in the same table, written with static `attribute_not_exists` conditions and no coordination logic |

`warehouses` follows the precedent of `orders` (a `Scan` with a `#Type` filter, ADR-0009 §3).

| Field | Operation |
|---|---|
| `createWarehouse` | `TransactWriteItems` (warehouse + guard) |
| `updateWarehouse` | `UpdateItem` (name, address, priority), `attribute_exists AND Type = Warehouse` → `NotFound` |
| `setWarehouseActive` | `UpdateItem` (`Active`), same condition |
| `warehouses` | `Scan` filtered on `Type`, sorted by priority then code. No pagination: there are tens of warehouses, not thousands |
| `warehouse` | `GetItem`, `null` unless `Type = Warehouse` |

Errors: `BadRequest` with `errorInfo.field` (the GraphQL path, e.g. `address.zipCode`),
`WarehouseCodeTaken`, `TransactionConflict` (retryable), `NotFound`, `Unauthorized`. `NotFound` is
PascalCase on purpose, like `BadRequest` and the checkout's error types (ADR-0050 §8), rather than
the older `NOT_FOUND` of `updateChallengePoints`.

The data source starts read-only, like every other (`dynamoDbDataSource` in `appsync-api.ts`), and is
granted exactly `dynamodb:PutItem` (what `TransactWriteItems` performs here) and `dynamodb:UpdateItem`
on `inventory-warehouses`, nothing wider. The validation rules live in the resolvers and are
mirrored, with cross-referencing comments, by `local.ts` (`lib/warehouse-validation.ts`) and by the
Blazor form, as `MAX_POINTS` is.

### 5. Local development

`Inventory.DevelopmentDataSeeder` creates `inventory-warehouses` in DynamoDB Local and seeds `ALPHA`
(priority 1) and `BETA` (priority 2), idempotently. The SPA's `local.ts` serves the five fields
against DynamoDB Local for the Blazor app, with the same validation and errors and, like the rest of
`local.ts`, no group check.

### 6. Direction for the next modules (amended as each is specified)

Recorded here so later work starts from it, not decided in detail:

- **Balances and ledger** (`inventory-movements`): balance per product × warehouse with `Available`
  and `Reserved`, never negative; every movement (receipt, adjustment, transfer) writes its ledger
  row in the same transaction that moves the balance.
- **Storefront handoff** (`catalog-stock-handoff`): Inventory publishes the available total by CDC.
  CatalogView consumes it ([ADR-0040](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md)):
  a plain 1:1 handler while Inventory has a single event, and its own dispatcher plus
  `IInventorySyncStrategy` once a second one exists (e.g. a warehouse deactivation). Catalog drops
  `Stock`, which amends [ADR-0032](./0032-create-product-with-price-step-functions-express-saga.md)
  (the `createProductWithPrice` saga input and task mapping carry `stock`).
- **Sales** (`checkout-stock-reservation`): reservation is a state of `checkout-saga`, as the Future
  Constraints of [ADR-0050](./0050-checkout-step-functions-express-saga.md) (Proposed) require:
  `OutOfStock` is reported through its §8 error contract, `ReleaseStock` compensates only typed
  refusals, and reservations orphaned by ambiguous failures are swept by the order-saga reconciler.
  Commit and release are steps of the order saga (ADR-0051, reserved and not yet written — see
  [SPEC-order-saga.md](../../SPEC-order-saga.md)), keyed by `OrderId`, and the allocation follows
  `Priority`.

---

## Consequences

### Positive

- The person who handles the goods owns the number, and the Seller no longer guesses it.
- Stock gets a location, and from module 2 on, a history.
- A code can never be taken twice, without a read-before-write or a Lambda.
- Module 1 adds no compute: one table, five direct resolvers and one group.

### Negative / Costs

- One more bounded context, stack and deploy workflow.
- Validation exists in three copies (resolver, `local.ts`, Blazor).
- `warehouses` is a `Scan`. It is fine at tens of items and has to change if warehouses ever number
  in the thousands.
- Warehouses cannot be deleted, so test warehouses created on dev stay there, deactivated.
- "Never deleted" holds within the stack's lifetime: like every DuckStore table, `inventory-warehouses`
  uses `RemovalPolicy.DESTROY` and no PITR, so deleting the stack deletes the history.

### Mitigation Strategies

- The three validation copies point at each other in comments, and the same cases run against the
  real APPSYNC_JS runtime (`aws appsync evaluate-code`) and the `local.ts` copy (unit tests).
- E2E warehouses carry an `E2E-` code prefix and are deactivated at the end of each test.

### Future Constraints

- Inventory's write path is `graphql/resolvers/inventory/**`, its seeder and (from module 2)
  `Inventory.Function`. No other context's resolver, Lambda or saga task MAY write its tables. Other
  contexts read the total through CatalogView (module 3) or call Inventory's Lambdas from a saga
  (module 4).
- A warehouse's `Code` MUST NOT become editable without replacing the guard in the same transaction
  (delete the old guard, put the new one).
- Allocation MUST stay behind a policy (`StockAllocationPolicy`) so "priority" can be replaced by
  "nearest warehouse" without touching balances or the sagas.

---

## As Built (module 1)

- Verified on the real APPSYNC_JS runtime with `aws appsync evaluate-code` (86 cases,
  `scripts/inventory/evaluate-resolvers.mjs`), against the local BFF (`scripts/inventory/local-api-checks.mjs`)
  and on dev by `tests/E2E/Inventory/Inventory.E2ETests` (12 tests: StockKeeper, Admin, Seller, customer).
- APPSYNC_JS refuses a comparator for `Array.prototype.sort` (no function may be passed as an
  argument) and silently ignored a named one during evaluation, so `warehouses` sorts on a string key
  (zero-padded priority, then code). Future resolvers that need an order should do the same.

---

## Applies To

- `infra/constructs/inventory-dynamodb.ts`, `infra/stacks/inventory-stack.ts`, `infra/bin/app.ts`
- `.github/workflows/deploy-inventory-cdk.yml`
- `infra/constructs/appsync-auth.ts` (`StockKeeper`), `infra/constructs/appsync-api.ts`
- `graphql/schema.graphql`, `graphql/resolvers/inventory/**`
- `src/Services/Inventory/Inventory.DevelopmentDataSeeder`, `src/AppHost/{InventoryExtensions,Program}.cs`, `DuckStore.slnx`
- `src/WebApps/Managment.Web.Blazor` (Warehouses pages, `CognitoGroups`)
- `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`, `lib/warehouse-validation.ts`
- `tests/E2E/Inventory`

## References

- [ADR-0009: AppSync Resolver Selection](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) — why every field here is direct
- [ADR-0019: Module-Oriented Service Structure](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) — the structure Inventory follows
- [ADR-0040: CatalogView Consumers by Producer](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md) — the shape of the module 3 consumer
- [ADR-0045: Challenges Bounded Context](./0045-challenges-bounded-context-server-side-grading.md) — precedent for a direct, Admin-gated management resolver (`updateChallengePoints`)
- [ADR-0032: Product Creation With Price as a Step Functions Express Saga](./0032-create-product-with-price-step-functions-express-saga.md) — loses `stock` in module 3
- [ADR-0050: Checkout as a Step Functions Express Saga](./0050-checkout-step-functions-express-saga.md) — where the stock reservation will live
- [SPEC-inventory.md](../../SPEC-inventory.md), [SPEC-inventory-warehouses.md](../../SPEC-inventory-warehouses.md)
