# ADR-0027: CatalogView — Product Search and Rating Aggregation via Amazon OpenSearch

## Status
**Superseded** — July 2026 — by [ADR-0030](./0030-catalogview-dynamodb-drop-opensearch.md). A
cost analysis of every way to actually run OpenSearch on AWS (managed domain, self-hosted EC2,
OpenSearch Serverless) found all three disproportionate to CatalogView's actual needs; CatalogView
is DynamoDB-backed instead, and `products`/`product` revert to Direct DynamoDB resolvers (§5 below
is reversed). This ADR is kept for historical context on the CDC event design
(`CatalogProductSyncEvent`, `PriceChangedEvent`), which ADR-0030 reuses unchanged.

Supersedes [ADR-0011](./0011-review-bounded-context-rating-aggregation-via-cdc.md) §4 (the
DynamoDB-based rating aggregation model in Catalog). ADR-0011 §1–3 (the `Review` bounded context,
its `reviews` table, and the `ReviewCreated` CDC publisher) are unaffected and reused as-is.

Amends [ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)'s
resolver decision table: `products(...)` and `product(id)` move from **Direct** to **Lambda**.

**Extended** — July 2026 — [ADR-0029](./0029-review-upsert-composite-key-and-rating-delta.md) adds
a sibling `ReviewUpdated` consumer and Painless script for the review-edit path, alongside (not
replacing) the `ReviewCreated` aggregation this ADR defines.

---

## Context

Catalog currently owns both the write side (`createProduct`/`updateProduct`/`deleteProduct`) and
the read/search side (`products`/`product`) of the product catalog, backed by a single DynamoDB
table. It also materializes the product's aggregate rating (`AverageRating`, `RatingCount`, and an
internal `RatingSum`) directly on the product item, updated by a `ReviewCreated` consumer Lambda
(ADR-0011 §4) via a two-step `TransactWriteItems` (increment) + `GetItem`/`UpdateItem` (recompute
average).

This has two growing costs:

- **No real search.** DynamoDB `Scan` with a `contains()` filter is the only "search" available
  today (see the categories/orders resolvers) — there is no full-text search, no relevance
  ranking, and filtering by a numeric range (e.g. "rating ≥ 4") over a `Scan` is both slow and
  expensive at any real catalog size.
- **Two-step, non-atomic rating aggregation.** ADR-0011 §4 itself calls out the negative: "a crash
  between the increment and the recompute leaves `AverageRating` one review stale". DynamoDB has
  no primitive that atomically increments a sum/count *and* recomputes a derived average in one
  write.

Both problems point to the same fix: a purpose-built search engine. **Amazon OpenSearch Service**
supports full-text search, numeric range queries, sorting, and — critically — a single atomic
scripted update (Painless) that can check an idempotency marker, accumulate a sum/count, and
recompute an average in one round-trip, replacing the two-step DynamoDB approach outright.

The free tier constrains the shape of the solution: a single `t3.small.search`/`t2.small.search`
node, ≤10GB gp2/gp3 storage, no Multi-AZ, no dedicated master, no UltraWarm, and (critically)
**no OpenSearch Serverless** — Serverless has no free tier. This is acceptable for a learning
project; it would not be a production HA posture.

---

## Decision

Introduce a new service, **CatalogView**, that owns product *search and read* — including the
aggregate rating — backed by a single-node, free-tier-eligible OpenSearch domain. Catalog keeps
ownership of product *writes* (`createProduct`/`updateProduct`/`deleteProduct` stay Direct
DynamoDB resolvers, unaffected) but drops `AverageRating`/`RatingCount` entirely; they exist only
as OpenSearch document fields from now on.

### 1. New event: `CatalogProductSyncEvent`

The existing `CatalogUpdatedEvent` (`ChangeType` + `ProductId` only) exists purely for SPA ISR tag
invalidation and deliberately carries no payload. Widening it would couple that unrelated concern
to CatalogView's indexing needs, so a **sibling event** is introduced instead:

```csharp
public record CatalogProductSyncEvent : IntegrationEvent
{
    public string ChangeType { get; init; }
    public string ProductId { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public string ImageUrl { get; init; }
    public int Stock { get; init; }
    public List<string> CategoryIds { get; init; }
}
```

It is published by a new `CatalogSearchSyncRule : IStreamRule<CatalogStreamImage>`, registered
alongside the existing `CatalogProductChangedRule` in Catalog's existing
`StreamRuleDispatcher<CatalogStreamImage>` (same `ProductStreamPublisher` Lambda, same `products`
Streams source — no new table or Lambda in Catalog). `CatalogStreamImage` widens from just `Id` to
project `Name`/`Description`/`ImageUrl`/`Stock`/`CategoryIds` from the Streams image, so
the rule can hydrate a complete event without a callback into Catalog.

The event carries **no price**: Catalog's product item no longer has one (ADR-0026 — Pricing owns
the nominal price). The price reaches the search document through a second CDC path instead:
Pricing's `prices` table gets a `NEW_IMAGE` stream and a `PriceStreamPublisher` Lambda that
publishes `PriceChangedEvent` (`ProductId` + `NominalPrice`) on INSERT/MODIFY; CatalogView's
`PriceSyncConsumer` merges the absolute value into the document via `ApplyPriceAsync` (a partial
`_update` touching only `price`). Setting an absolute price is naturally idempotent, so no inbox
or event-id guard is needed. REMOVE is not published: a price row only disappears when the product
is removed, and the document is deleted via `CatalogProductSyncEvent` in that case. This realizes
the "a future CatalogView is expected to join the two" note in ADR-0026/the GraphQL schema.

### 2. CatalogView has no DynamoDB table of its own

CatalogView's only datastore is the OpenSearch `products` index. There is nothing to seed by
identity/PK lookups and no idempotency inbox table:

- **Product sync is naturally idempotent.** `CatalogProductSyncConsumer` upserts (INSERT/MODIFY)
  or deletes (REMOVE) a document keyed by `ProductId` — replaying the same event is a no-op change
  in effect.
- **Rating aggregation idempotency is native to OpenSearch.** Each document carries an internal
  `lastRatingEventId` field. `ReviewAggregateConsumer` applies a single Painless scripted update
  via the OpenSearch `_update` API:

```painless
if (ctx._source.lastRatingEventId == params.eventId) {
    ctx.op = 'none';
} else {
    ctx._source.ratingSum = (ctx._source.ratingSum ?: 0) + params.rating;
    ctx._source.ratingCount = (ctx._source.ratingCount ?: 0) + 1;
    ctx._source.averageRating = (double) ctx._source.ratingSum / ctx._source.ratingCount;
    ctx._source.lastRatingEventId = params.eventId;
}
```

This is strictly simpler than the DynamoDB approach it replaces: one atomic script does the
idempotency check, the accumulation, *and* the average recompute — no crash window between steps.
`ratingSum` remains internal (never returned to GraphQL callers), mirroring the old `RatingSum`
DynamoDB attribute's role.

### 3. Product sync must never clobber the rating fields

`CatalogProductSyncConsumer`'s upsert uses OpenSearch's partial `_update` with `doc_as_upsert:
true` (a field-merge), not a full document `Index`/PUT — an unrelated product edit (stock,
description) must never reset `averageRating`/`ratingCount`/`ratingSum`/`lastRatingEventId`, nor
the `price` maintained by `PriceSyncConsumer`. This is the same invariant Catalog's
`DynamoProductRepository.ToItem` used to protect before this ADR; it now lives in
`OpenSearchProductIndex.UpsertAsync`, whose merge deliberately excludes `price`.

### 4. Historical backfill — an explicit, scoped exception

`CatalogView.DevelopmentDataSeeder` runs once (locally and on first production rollout, never
again afterward) to:

1. Create the OpenSearch `products` index with an explicit mapping (`name`/`description` as
   `text`; `price`/`stock`/`averageRating`/`ratingCount`/`ratingSum` as numeric; `categoryIds` as
   `keyword`; no full review text is ever indexed — free-tier storage discipline).
2. Scan Catalog's `products` table for product fields, Review's `reviews` table (grouped by
   `ProductId`) to compute historical `ratingSum`/`ratingCount`/`averageRating`, **and** Pricing's
   `prices` table for nominal prices (ADR-0026 — products no longer carry a price) — so neither
   ratings nor prices are silently zeroed for existing products — then bulk-index complete
   documents via the OpenSearch `_bulk` API.

This is a deliberate, one-time exception to "each bounded context owns its table, no cross-context
reads": it is a migration tool executed by an operator/seeder, not a runtime dependency between
services. No Lambda or steady-state code path reads another context's table.

### 5. AppSync resolver reclassification (amends ADR-0009)

| Field | Previously | Now | Escalation criterion |
|---|---|---|---|
| `products(query, sortBy, minRating, maxRating, pageSize, nextToken)` | Direct | **Lambda** | ADR-0009 Criterion 3 — external, non-DynamoDB integration (OpenSearch) |
| `product(id)` | Direct | **Lambda** | ADR-0009 Criterion 3 — external, non-DynamoDB integration (OpenSearch) |

`createProduct`/`updateProduct`/`deleteProduct` are unaffected — they remain Direct DynamoDB
resolvers against Catalog. The GraphQL schema's `Query.products` gains `query: String`, `sortBy:
ProductSort`, `minRating: Float`, `maxRating: Float` arguments; `type Product` keeps
`averageRating`/`ratingCount`, now sourced from CatalogView.

### 6. Free-tier discipline

The local dev OpenSearch container and any eventual `AWS::OpenSearchService::Domain` MUST stay
within: single node (`t3.small.search`/`t2.small.search`), single AZ, no dedicated master, no
UltraWarm/cold storage, never `AWS::OpenSearchService::ServerlessCollection` (no free tier). This
repository has no CDK/Terraform for AWS provisioning today — only Aspire for local dev — so actual
production provisioning of the domain, along with CloudWatch log retention (7–14 days) on the four
new Lambdas' log groups, is an **infra follow-up**, tracked here rather than invented ad hoc in a
tool that doesn't otherwise exist in this repo.

### 7. Rollout order

1. Deploy CatalogView "dark" (OpenSearch domain + Lambdas), nothing flowing yet.
2. Enable `CatalogSearchSyncRule` in Catalog — new products start syncing; Catalog still carries
   its (soon-to-be-removed) rating fields as a transitional safety net.
3. Run the one-time backfill (products + historical reviews).
4. Validate CatalogView's computed averages against Catalog's existing ones.
5. Cut over the `products`/`product` AppSync resolvers to CatalogView's Lambdas (a single deploy,
   not a feature flag — rollback is a resolver revert).
6. Only once stable: remove the `ReviewCreatedConsumer` and rating fields from Catalog for good
   (a point of no return — Catalog can no longer reconstruct ratings after this).
7. This ADR moves Proposed → Accepted; ADR-0011 is marked Superseded for its §4 content.

---

## Applies To

- `src/Services/Catalog/Catalog.Function` — `Product` entity and `DynamoProductRepository` drop
  rating fields; `ReviewCreatedConsumer` deleted; `CatalogStreamImage` widened;
  `CatalogSearchSyncRule` added to the existing stream publisher.
- `src/Services/Catalog/Catalog.DevelopmentDataSeeder` — no longer provisions
  `catalog-processed-events`.
- `src/Services/CatalogView/CatalogView.Function` — new service: `CatalogProductSyncConsumer`,
  `ReviewAggregateConsumer`, `SearchProducts`, `GetProduct` Lambdas; `OpenSearchProductIndex`.
- `src/Services/CatalogView/CatalogView.DevelopmentDataSeeder` — new: index creation + one-time
  backfill.
- `src/BuildingBlocks/BuildingBlocks.Messaging/Events/CatalogProductSyncEvent.cs` — new shared
  contract.
- `src/AppHost` — `CatalogViewExtensions.cs`, `CatalogExtensions.cs`, `Program.cs`.
- `src/WebApps/Shopping.Web.SPA.React/graphql` — `schema.graphql`,
  `resolvers/Query.products.js`, `resolvers/Query.product.js`, `app/api/graphql/local.ts`.

---

## Consequences

### Positive

- **Real search** — full-text relevance ranking and numeric range filtering (rating), which a
  DynamoDB `Scan` cannot do at any reasonable scale or cost.
- **Simpler, truly atomic rating aggregation** — one Painless script replaces a two-step DynamoDB
  transaction + recompute, closing the staleness window ADR-0011 §4 called out as a known cost.
- **No new idempotency infrastructure** — the rating idempotency marker lives on the document
  itself; CatalogView needs no DynamoDB table at all.
- **Context boundaries preserved for steady-state traffic** — the one-time backfill is the only
  cross-context table read, and it is a migration tool, not a runtime coupling.

### Negative / Costs

- **A second datastore and its own operational surface** — OpenSearch domain provisioning,
  patching, and monitoring, on top of DynamoDB, EventBridge, and Elasticsearch (used only for
  logging/Kibana, unrelated to this data).
- **Eventual consistency, twice over** — product edits and rating updates both propagate via CDC
  (Streams → EventBridge) before they're visible in search results; same acceptable-for-a-demo
  trade-off as ADR-0011's original design.
- **Free tier is time-boxed** — the OpenSearch free tier only applies for the first 12 months of
  an AWS account; this ADR does not change that constraint, only respects it while it lasts.
- **No .NET OpenSearch client with an official net10.0 target yet** — `OpenSearch.Net` 1.8.0's
  newest listed TFM is `net8.0` (plus `netstandard2.0/2.1`); it restores and runs fine under
  net10.0 today, but this should be revisited when a net10.0-targeted release ships.
- **Two-step consumer semantics still apply to product sync clobbering** — `UpsertAsync` must
  always use partial merge (`doc_as_upsert`), never a full `Index`; a future change that
  "simplifies" this back to a full-document PUT would silently zero every product's rating on its
  next edit. This is called out explicitly in code comments and here.

### Mitigation Strategies

- Keep the Painless script and the partial-merge invariant covered by unit tests at the handler
  level (mocking `IProductSearchIndex`), so the idempotency and non-clobbering behavior is
  verified without needing a live OpenSearch cluster.
- Track the "no CDK/IaC in this repo" gap as an explicit infra follow-up (§6) rather than
  inventing a one-off provisioning script that wouldn't match how the rest of the project deploys.

### Future Constraints

- Any new field added to `SearchDocument` that should be publicly queryable MUST be added to the
  index mapping in `CatalogView.DevelopmentDataSeeder` *and* to the GraphQL `Product` type in the
  same change — the mapping is not auto-inferred for fields the seeder doesn't already write.
- New AppSync fields against CatalogView MUST be classified against ADR-0009 §2 same as any other
  field — OpenSearch integration is the criterion that justifies Lambda, not "it's the search
  service" as a blanket exemption.

---

## References

- [ADR-0011: Review Bounded Context — Product Ratings Aggregated into Catalog via CDC](./0011-review-bounded-context-rating-aggregation-via-cdc.md)
- [ADR-0009: AppSync Resolver Selection — Direct DynamoDB Resolvers as Default](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC)](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- `Ordering.OrderStreamPublisherFunction`/`OrderCreatedRule` — reference implementation for the
  `IStreamRule<TImage>` + `StreamRuleDispatcher<TImage>` pattern reused here.
- [ADR-0028: Gateway Cost Table and Payment Highlights](./0028-gateway-cost-table-and-payment-highlights.md) —
  extends `SearchDocument` with `avistaPrice`/`maxInstallmentsWithoutInterest`/`installmentValue`,
  extending the existing `PriceChangedEvent`/`PriceSyncConsumer` (one event, one merge) rather than
  adding a parallel event/consumer — following this ADR's partial-merge invariant and Future
  Constraints rule.
