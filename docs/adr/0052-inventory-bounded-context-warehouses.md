---
tags:
  - status/accepted
  - domain/inventory
  - domain/catalog
  - domain/appsync
---

# ADR-0052: Inventory Bounded Context — Stock per Warehouse, Owned by the StockKeeper

## Status
**Accepted** — October 2026. The warehouses (§2–§5) and the stock balances and movements ledger (§7)
are implemented; §6 records the direction for the storefront total and sales, and is amended as each
is decided in detail.

**Amended** — October 2026: §7 decides the stock balances and the movements ledger (receipts,
adjustments, transfers).

**Amended** — October 2026 (proposed): §8 makes Inventory the source of the storefront's stock and
takes `stock` out of Catalog. It amends [ADR-0032](./0032-create-product-with-price-step-functions-express-saga.md)
§4/§6 and [ADR-0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
(fan-out rules).

ADR-0051 is reserved for the order orchestration, which is why this one is 0052.

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
starts reading Inventory's total (§6). Until then, the two coexist and nothing reads Inventory from
the storefront.

Inventory follows the reference structure ([ADR-0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)):
its own DynamoDB tables, CDK stack (`DuckStoreInventoryStack`) and `src/Services/Inventory/`. Its
`.NET Inventory.Function` project is created only when the first Lambda appears, with one module per
aggregate: `Modules/Warehouses` and `Modules/Stock`. The warehouses are entirely direct resolvers.

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

| Group | Warehouses (read) | Warehouses (write) | Stock movements | Stock total |
|---|---|---|---|---|
| `StockKeeper` | Yes | Yes | Yes (§7) | Yes |
| `Admin` | Yes | Yes | Yes (§7) | Yes |
| `Seller` | No | No | No (not even reads, §7) | Read only |
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

### 6. Direction for stock, the storefront total and sales

Recorded here so later work starts from it, not decided in detail:

- **Balances and ledger**: decided in §7.
- **Storefront total**: decided in §8.
- **Sales**: reservation is a state of `checkout-saga`, as the Future
  Constraints of [ADR-0050](./0050-checkout-step-functions-express-saga.md) (Proposed) require:
  `OutOfStock` is reported through its §8 error contract, `ReleaseStock` compensates only typed
  refusals, and reservations orphaned by ambiguous failures are swept by the order-saga reconciler.
  Commit and release are steps of the order saga (ADR-0051, reserved), keyed by `OrderId`, and the
  allocation follows `Priority`.

### 7. Stock balances and the movements ledger

**Tables.** Both are owned by Inventory and use the same settings as `inventory-warehouses`
(`PAY_PER_REQUEST`, `DESTROY`, no PITR); every index projects `ALL`, so no read needs a second
lookup. Neither has a stream yet: the storefront total (§6) adds one to `inventory-stock`.

| Table | Key | Indexes | Holds |
|---|---|---|---|
| `inventory-stock` | PK `ProductId`, SK `WarehouseId` | GSI1: PK `WarehouseId`, SK `ProductId` | The balance: `Available`, `Reserved` (0 until sales reserve, §6), `WarehouseCode` (immutable, so safe to copy), `UpdatedAt`. A row is born with the first movement that brings stock into its warehouse |
| `inventory-movements` | PK `ProductId`, SK `MovementId` | LSI1: SK `CreatedAt` (a product's history) · GSI1: PK `WarehouseId`, SK `CreatedAt` (a warehouse's history) | The ledger: one immutable row per change of one balance |

**Invariant.** For every product × warehouse, the sum of `Quantity` over its ledger rows equals the
stock on hand, `Available + Reserved`, and `Available` is never negative. A reservation (§6) moves
units between `Available` and `Reserved` without changing what is on hand, so it writes no ledger
row; units leaving the warehouse do. Every on-hand change writes its ledger row in the same
transaction; no row is ever updated or deleted, and a mistake is corrected with an opposite
adjustment.

| Movement | Ledger rows | Rules |
|---|---|---|
| Receipt | one `Receipt`, `+qty` | The warehouse MUST be active. `InvoiceNumber` (1–60 chars) is required; `Supplier` (≤ 120) and `Note` (≤ 500) are optional |
| Adjustment | one `Adjustment`, `±qty` (a `Direction` of `Increase` or `Decrease` plus a positive quantity) | `Reason` ∈ `Count`, `Loss`, `Damage`, `Return`, `Other`; `Note` is required for `Other`. An increase needs an active warehouse; a decrease is allowed on an inactive one |
| Transfer | `TransferOut` `−qty` at the origin and `TransferIn` `+qty` at the destination, sharing `TransferId` | Origin ≠ destination. The destination MUST be active; the origin may be inactive, which is how a warehouse is emptied before it closes |

Quantities are integers from 1 to 100,000. Every row carries `MovementId`, `ProductId`, `WarehouseId`,
`WarehouseCode`, `Type`, the signed `Quantity`, `BalanceAfter` (on hand right after it), the fields
of its type (`InvoiceNumber`, `Supplier`, `Reason`, `Note`, `TransferId`, `CounterpartWarehouseId`),
`PayloadHash`, `CreatedAt` and `CreatedBy` (the Cognito `sub` from the token, never from the client;
there is no e-mail lookup).

The Lambdas' IAM grants match what the transaction does — reads, `UpdateItem` on the balance,
`PutItem` of the ledger row, `ConditionCheckItem` on the warehouse — with no `DeleteItem`, so IAM
itself keeps the ledger immutable.

**Lambdas, one per movement.** `inventory-record-stock-receipt`, `inventory-record-stock-adjustment`
and `inventory-transfer-stock`, in a new `Inventory.Function` (Inventory's first Native AOT package,
ADR-0042), module `Modules/Stock` with the rules in the domain (`StockBalance`), not in handlers; they
read warehouses through `Modules/Warehouses/Data`. `Modules/Stock` is a deliberate singular, fixed in
§1. ADR-0009 classification: criterion 1 for all three — the warehouse and the balance are read before
writing, which ADR-0048 §2's no-read `ADD … Available >= :qty` could not replace, because the
response carries `BalanceAfter`, a refusal carries `available`, and a replay is compared against the
stored request — and criterion 4 for the transfer (two balances, two aggregates, one transaction).
They have no Function URL and are invoked only by AppSync (and, locally, `local.ts`), as ADR-0050 §2
requires of the checkout path.

**Replay first, then one transaction with optimistic concurrency.** Each Lambda:

1. Reads, consistently, every ledger row the key could have produced for this product (`<key>`,
   `<key>#OUT`, `<key>#IN`). If they are the request's own rows, it compares their `PayloadHash`
   with the request: the same request returns the recorded movement, a different one is refused
   with `IdempotencyKeyReused`; rows of another kind of movement are `IdempotencyKeyReused` too.
   Nothing else runs, so a retry of a movement that already committed is never re-judged against
   the stock it changed.
2. Reads the warehouse(s) and balance(s) consistently and lets `StockBalance` decide.
3. Writes one `TransactWriteItems`:

```text
Update inventory-stock      Available = :new ...   Condition: Available = :read AND Reserved = :reserved   (attribute_not_exists when the read found no row)
Put    inventory-movements  <ledger row>           Condition: attribute_not_exists(MovementId)
ConditionCheck inventory-warehouses                 Condition: #Type = Warehouse [AND Active = true where the rules require it]
```

A transfer has two `Update`s, two `Put`s and two `ConditionCheck`s (origin: `Type` only; destination:
`Type` and `Active`). On cancellation, a failed `Put` condition takes precedence — the request is a
replay that raced its twin, answered as in step 1 — and after any other cancellation step 1 runs
again too, because DynamoDB may report a twin's commit as a conflict rather than a failed condition;
then a failed warehouse check or a failed balance condition (or a `TransactionConflict`) sends the Lambda back to step 2, at most three times in all;
after that it refuses with the retryable `TransactionConflict`. The condition covers `Reserved` as well
as `Available`, so a reservation settling concurrently (§6) also forces a re-read, and each attempt
takes its own `CreatedAt`; `BalanceAfter` is therefore always exact and a warehouse's history reads
as a running sum.

**Idempotency.** Each mutation takes an `idempotencyKey`, a UUID (anything else is `BadRequest`, so a
client key can never collide with a `#OUT`/`#IN` suffix). The key is scoped to the product: the
ledger is keyed by `ProductId`, so the same key on another product is a new movement (the client
mints one key per form, which never changes product). `MovementId = idempotencyKey`; a transfer's
rows are `<key>#OUT` and `<key>#IN`. `PayloadHash` is a SHA-256 of the movement type and the
normalised input without the key (product, warehouse(s), quantity, direction, reason, invoice,
supplier, note), stored on every row of the movement. The client renewal rule is the `checkoutId`
contract of [ADR-0050](./0050-checkout-step-functions-express-saga.md) §3 (Proposed), restated here:
the client mints the key when the form opens, renews it **only** after a success or a typed refusal,
and keeps it after `InternalError`, a timeout or a network error, so the retry finds the movement if
it was recorded. After `IdempotencyKeyReused` the UI renews the key and tells the user the earlier
attempt may already be recorded.

**Product existence is checked by AppSync, not by Inventory, and only where stock comes in.** The
receipt and the adjustment are pipelines: the group check (StockKeeper or Admin), a `GetItem` on
Catalog's `products` table through the existing `ProductsDS` (`ProductNotFound`) — skipped for a
decrease — then the Lambda. The transfer skips the check. A product deleted from Catalog can still
be adjusted down to zero or moved, but never restocked. `ProductsDS` has the write grant Catalog's
own mutations need; Inventory's steps only read through it. Inventory never reads Catalog's table.
The check and the write are not atomic: a product deleted in between keeps its stock, which the same
rule lets anyone adjust away.

**Refusals are data.** The Lambdas return `{ Refusal: { ErrorType, Message, Field?, Available? } }`
for expected outcomes (`BadRequest`, `WarehouseNotFound`, `WarehouseInactive`, `InsufficientStock`
with `errorInfo.available`, `IdempotencyKeyReused`, `TransactionConflict`), as ADR-0050 §10 does; the
resolver raises them with that `errorType` and `errorInfo`. An unhandled exception is raised as
`InternalError` with no detail (not the exception message, unlike ADR-0009 §4's sample), so refusals
never count as Lambda errors and stack traces never reach the client.

**Queries are direct** (no criterion applies), StockKeeper or Admin only:

| Field | Operation |
|---|---|
| `stockByProduct(productId)` | `Query` on `inventory-stock` by `ProductId`: one row per warehouse, not paginated (like `warehouses`), with `totalAvailable` summed over every warehouse, inactive ones included — the stock keeper's view. The storefront total (§6) counts active warehouses only |
| `stockByWarehouse(warehouseId, pageSize, nextToken)` | GSI1 of `inventory-stock`, then product names |
| `stockMovementsByProduct(productId, pageSize, nextToken)` | LSI1 of `inventory-movements`, newest first |
| `stockMovementsByWarehouse(warehouseId, pageSize, nextToken)` | GSI1 of `inventory-movements`, newest first, then product names |

`pageSize` is 1–100 (default 20), which keeps a page within `BatchGetItem`'s 100-key limit. The
product-name step is a second operation (`BatchGetItem` on `products`), which ADR-0009 §3 lists under
criterion 1; it stays direct because it only decorates rows with display names and decides nothing,
as the direct `createReview` pipeline (ADR-0029) does. It deduplicates the `ProductId`s, skips the
call on an empty page, and returns `null` for a name it could not read (a deleted product or an
unprocessed key). The data sources for `inventory-stock` and `inventory-movements` are read-only; only
`Inventory.Function` writes those tables. The warehouses keep their §4 grants.

**Local development.** The seeder creates both tables and seeds no balance. `local.ts` checks the
product and invokes the real Lambdas through the Aspire Lambda emulator, as the checkout does,
instead of copying the rules; the author is `local-dev`. The queries are emulated against DynamoDB
Local.

### 8. The storefront total

**Inventory is the only source of the storefront's stock.** The number the storefront shows is
`Available` summed over the product's balances in **active** warehouses; stock in an inactive
warehouse never counts. Catalog no longer has `stock`: it leaves the `Product` entity, its table,
`ProductSyncedEvent`, the `createProduct`/`updateProduct`/`createProductWithPrice` inputs, the
`product-create-saga` (amending ADR-0032 §4 and §6) and the Blazor product form. The GraphQL
`Product` type keeps `stock`, read from CatalogView as before; a product with no total yet reads 0.

**The event.** `ProductStockChangedEvent { ProductId, Available, Version }` (ADR-0031: named after the
occurrence). Its `detail` is `{"ProductId":"<guid>","Available":70,"Version":12}` plus the
`IntegrationEvent` envelope (`Id`, `OccurredOn`, `EventType`).
`Version` orders totals without a clock:

- every `inventory-stock` write does `ADD Version :one` on its row, and `setWarehouseActive` does
  the same on the warehouse (other warehouse edits cannot change a total and leave it alone);
- a total's `Version` is the sum of the counters of the product's balance rows and of their
  warehouses, inactive ones included. A row or warehouse without the attribute counts 0;
- every change that can move the total adds one to a counter in that sum and none is ever
  decremented, so a newer state always has a greater `Version`. Two reads of the same state agree
  on both numbers.
- the publishers read the balances and then the warehouses, so one read is not a single snapshot.
  If a movement and a toggle land between the two reads, a total can be briefly wrong. It corrects
  itself: the publisher triggered by the last write reads the final state, at the greatest
  `Version`.

Timestamps were rejected for this: two writes in the same millisecond, or AppSync's and the Lambdas'
clocks a little apart, would leave an older total looking newer.

**Two publishers, both CDC (ADR-0005), both `Inventory.Function`, both through
`StreamRuleDispatcher` (ADR-0019):**

| Lambda | Source | Rule |
|---|---|---|
| `inventory-stock-stream-publisher` | `inventory-stock` stream (`NEW_AND_OLD_IMAGES`), `INSERT`/`MODIFY` | `ProductStockChangedRule`: the product's total. A product repeated in a batch yields identical events, which the consumer drops |
| `inventory-warehouses-stream-publisher` | `inventory-warehouses` stream (`NEW_AND_OLD_IMAGES`), `MODIFY` of a `Type = Warehouse` item whose `Active` changed; guards, new warehouses and other edits match nothing | `WarehouseActivationRule`: one total per product in that warehouse (GSI1 of `inventory-stock`, paginated) |

One record becoming N events is new to the dispatcher, so `BuildingBlocks.Messaging` gains
`IStreamFanOutRule<TImage>` (`Match` + `BuildAllAsync`, returning a list of `PublishInstruction`),
dispatched next to the `IStreamRule`s and published in the same `PublishManyAsync` call. This
amends ADR-0019's "a rule MUST return a `PublishInstruction`".

Both rules compute the total with one domain service (`StorefrontStock`), from consistent reads of
`inventory-stock` and `inventory-warehouses`. A failing batch is bisected and retried 3 times, then
lands in the `inventory` context DLQ with its alarm on `duckstore-alerts` (ADR-0015).

**The consumer.** `catalogview-inventory-sync-consumer`, a 1:1 handler: Inventory has a single event,
the case [ADR-0040](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md)
allows without a dispatcher. `IProductSearchIndex.ApplyStockAsync` writes:

```text
UpdateItem catalogview-products  SET Stock = :available, StockVersion = :version
Condition: attribute_not_exists(StockVersion) OR StockVersion < :version
```

- A condition failure means the stored total is at least as new: the event is late or repeated and
  is dropped without an error, since retrying would never succeed.
- There is no `attribute_exists(Id)`. A total that reaches CatalogView before the product's
  `ProductSynced` is kept on a stock-only item, as `ApplyPricingAsync` already does with a price. The
  product sync then completes the item: it writes `Stock = if_not_exists(Stock, 0)`, and the backfill
  seeder writes no `Stock` at all. The backfill rewrites whole items, so it is meant for an empty
  table. Re-running it after the rollout resets every product to 0, and removes `StockVersion`,
  until that product's next movement or warehouse toggle.
- A product deleted from Catalog may still move stock out (§7), and a warehouse toggle reaches it.
  Its totals land on a stock-only item. Nothing retries, and nothing reaches the DLQ for it.
- An item without a `Name` is not a product. `product(id)` returns `null` for it, and `products`
  leaves it out (it has no `GSI1PK`, and the search filter needs a `Name`). `stock` reads
  `Stock ?? 0`.

Other failures follow CatalogView's consumers: 2 async-invoke retries, then the `catalogview` context
DLQ. EventBridge delivery failures go to the same queue. The existing `CatalogViewProductSyncedEvent`,
published on any write to the item (ADR-0035), already revalidates the product pages. A stock-only
item triggers it too, which is harmless.

**Rollout order (binding).**

1. CatalogView stops writing `Stock` from `ProductSyncedEvent` and starts consuming
   `ProductStockChangedEvent`.
2. Inventory publishes.
3. The dev showcase is seeded.
4. Catalog stops sending `Stock`, and the inputs lose it together with the Blazor form.

Until a product's first total arrives, CatalogView still holds the last value Catalog sent. The
rollout is therefore complete only when every product either has a `StockVersion` or shows 0. On
dev, the showcase seeds an Inventory receipt for every product above 0, and the seeding script
checks that nothing else is left.

**Local development.** EventBridge is best-effort locally, so `local.ts` applies the same total (a
tested copy of `StorefrontStock`) to `catalogview-products` with the same condition. It does so after
each movement and, for every product of the warehouse, after `setWarehouseActive`, as it already
simulates the review credit. The Inventory seeder records the Catalog seed products' former stock as
receipts into ALPHA, through the receipt handler (so with ledger rows), and applies their totals the
same way, after CatalogView's backfill.

---

## Consequences

### Positive

- The person who handles the goods owns the number, and the Seller no longer guesses it.
- Stock gets a location and, with the ledger (§7), an immutable history that always adds up to the
  balance.
- A code can never be taken twice, without a read-before-write or a Lambda.
- The warehouses add no compute: one table, five direct resolvers and one group.

### Negative / Costs

- The storefront's stock is eventually consistent with Inventory (seconds, through EventBridge).
- Deactivating a warehouse publishes one event per product it holds, and reads every one of their
  totals.
- A balance created in a warehouse at the same moment that warehouse is toggled may be missed by the
  fan-out, because GSI1 is eventually consistent. The total stays wrong until that product's next
  movement.
- Totals of deleted products and early totals leave stock-only items in `catalogview-products`,
  which nothing reads.
- Movements need Lambdas (§7): Inventory's first Native AOT package and a linux-arm64 publish in its
  workflow.
- The history shows who made a movement as a Cognito `sub`, not a name or e-mail.
- Test movements on dev cannot be removed through the application (the ledger is immutable).
- One more bounded context, stack and deploy workflow.
- Validation exists in three copies (resolver, `local.ts`, Blazor).
- `warehouses` is a `Scan`. It is fine at tens of items and has to change if warehouses ever number
  in the thousands.
- Warehouses cannot be deleted, so test warehouses created on dev stay there, deactivated.
- "Never deleted" and "immutable" hold within the stack's lifetime: like every DuckStore table,
  `inventory-warehouses`, `inventory-stock` and `inventory-movements` use `RemovalPolicy.DESTROY` and
  no PITR, so deleting the stack deletes the history.

### Mitigation Strategies

- The three validation copies point at each other in comments, and the same cases run against the
  real APPSYNC_JS runtime (`aws appsync evaluate-code`) and the `local.ts` copy (unit tests).
- E2E warehouses carry an `E2E-` code prefix and are deactivated at the end of each test; E2E stock
  movements happen only in those warehouses, so the dev history of real warehouses stays clean.
- The fan-out's GSI race needs a balance write and a toggle of its warehouse within the index's
  propagation delay (well under a second). The stock-stream publisher reads the base table
  consistently, so it covers every case except the one where the toggle lands after its read.

### Future Constraints

- Inventory's write path is `graphql/resolvers/inventory/**` (warehouses), `Inventory.Function`
  (`inventory-stock` and `inventory-movements`, nothing else writes them) and its seeder. No other
  context's resolver, Lambda or saga task MAY write its tables. Other contexts read the total through
  CatalogView or call Inventory's Lambdas from a saga (§6).
- Ledger rows MUST NOT be updated or deleted, and every on-hand change MUST write its ledger row in
  the same transaction (the ADR-0048 rule for points). The seeder MUST NOT seed balances without
  ledger rows.
- Inventory's Lambdas MUST NOT get a Function URL or any entry point other than AppSync, their own
  tables' DynamoDB Streams (§8) and the local `local.ts` emulation.
- A warehouse's `Code` MUST NOT become editable without replacing the guard in the same transaction
  (delete the old guard, put the new one).
- The storefront's stock MUST come only from Inventory through `ProductStockChangedEvent`; Catalog
  MUST NOT regain `stock`, and CatalogView MUST NOT read Inventory's tables. A total is applied only
  under the `Version` condition, and every Inventory write that can change a total MUST add one to
  the `Version` of its balance row or warehouse.
- Allocation MUST stay behind a policy (`StockAllocationPolicy`) so "priority" can be replaced by
  "nearest warehouse" without touching balances or the sagas.

---

## As Built

- Verified on the real APPSYNC_JS runtime with `aws appsync evaluate-code` (138 cases over the 12
  Inventory resolvers, `scripts/inventory/evaluate-resolvers.mjs`), against the Lambdas in the Aspire
  emulator (`scripts/inventory/local-lambda-checks.mjs`, concurrent movements included) and the local
  BFF (`scripts/inventory/local-api-checks.mjs`), by `Inventory.UnitTests` (domain, repository,
  recorder), and on dev by `tests/E2E/Inventory/Inventory.E2ETests` (24 tests: StockKeeper, Admin,
  Seller, customer — the ledger checked against the balance after every flow).
- The Lambdas' JSON omits null fields (`Refusal` on success, `Movements` on a refusal, `Available` on
  most refusals); the resolvers treat absent and null alike.
- `Array.prototype.sort` takes no comparator in APPSYNC_JS: an inline one fails the runtime's
  `no-function-passing` lint, and a named one was silently ignored during evaluation (callbacks to
  `map`/`some` are fine). `warehouses` therefore sorts on a string key (zero-padded priority, then
  code). Future resolvers that need an order should do the same.

---

## Applies To

- `infra/constructs/inventory-dynamodb.ts`, `infra/stacks/inventory-stack.ts`, `infra/bin/app.ts`
- `.github/workflows/deploy-inventory-cdk.yml`
- `infra/constructs/appsync-auth.ts` (`StockKeeper`), `infra/constructs/appsync-api.ts`
- `graphql/schema.graphql`, `graphql/resolvers/inventory/**`
- `src/Services/Inventory/Inventory.DevelopmentDataSeeder`, `src/Services/Inventory/Inventory.Function`, `src/AppHost/{InventoryExtensions,Program}.cs`, `DuckStore.slnx`
- `infra/constructs/inventory-lambdas.ts`, `tests/Services/Inventory/Inventory.UnitTests`
- `src/WebApps/Managment.Web.Blazor` (Warehouses and Stock pages, `CognitoGroups`)
- `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`, `lib/warehouse-validation.ts`, `lib/storefront-stock.ts`
- `tests/E2E/Inventory`
- Storefront total (§8): `src/BuildingBlocks/BuildingBlocks.Messaging` (`ProductStockChangedEvent`, `IStreamFanOutRule`),
  `src/Services/CatalogView` (`InventorySync` consumer, `DynamoProductIndex`, the backfill seeder),
  `infra/constructs/catalogview-lambdas.ts`, `src/AppHost/CatalogViewExtensions.cs`,
  `graphql/resolvers/products/**`, `infra/constructs/product-create-saga.ts`, `src/Services/Catalog`,
  `src/WebApps/Managment.Web.Blazor` (product form), `scripts/inventory/seed-dev-showcase.mjs`

## References

- [ADR-0009: AppSync Resolver Selection](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) — direct warehouses and queries; Lambda movements (§7)
- [ADR-0019: Module-Oriented Service Structure](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) — the structure Inventory follows; amended by §8 with fan-out rules
- [ADR-0040: CatalogView Consumers by Producer](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md) — the shape of the storefront-total consumer
- [ADR-0045: Challenges Bounded Context](./0045-challenges-bounded-context-server-side-grading.md) — precedent for a direct, Admin-gated management resolver (`updateChallengePoints`)
- [ADR-0032: Product Creation With Price as a Step Functions Express Saga](./0032-create-product-with-price-step-functions-express-saga.md) — loses `stock` with the storefront total (§8)
- [ADR-0050: Checkout as a Step Functions Express Saga](./0050-checkout-step-functions-express-saga.md) — where the stock reservation will live; the idempotency-key and refusal contracts §7 follows
- [ADR-0048: Points Transactions Ledger](./0048-points-transactions-ledger-and-in-cart-redemption.md) — the ledger precedent (same-transaction balance change, conditional ids)
- [ADR-0042: Native AOT ZIPs](./0042-lambda-native-aot-zip-provided-al2023.md) — how `Inventory.Function` is packaged
- [ADR-0029: Review Upsert](./0029-review-upsert-composite-key-and-rating-delta.md) — the direct pipeline precedent
- [ADR-0030: CatalogView Goes DynamoDB-Backed](./0030-catalogview-dynamodb-drop-opensearch.md) — the table the storefront total is written to
- [ADR-0035: CatalogView-Owned CDC Events](./0035-catalogview-owned-cdc-events-drive-spa-revalidation.md) — revalidates the product pages when the total changes
- [ADR-0015: SQS DLQs for CDC Publishers and Consumers](./0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md) — the failure path of both publishers and the consumer
