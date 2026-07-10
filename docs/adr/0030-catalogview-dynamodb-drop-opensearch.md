# ADR-0030: CatalogView Goes DynamoDB-Backed — Drop OpenSearch

## Status
**Accepted** — July 2026

Supersedes [ADR-0027](./0027-catalogview-opensearch-product-search-and-rating-sync.md) in full —
CatalogView keeps its role (product read/search + rating aggregation), but its datastore and the
`products`/`product` AppSync resolver classification both change. ADR-0027 §1–4/§7 (the
`CatalogProductSyncEvent`, `PriceChangedEvent` CDC path, and rollout ordering) are reused as-is —
only the storage engine underneath `IProductSearchIndex` and the two resolvers change. ADR-0029
(review-edit rating delta) is unaffected: `ApplyRatingUpdateAsync`'s contract and idempotency
guard carry over unchanged, now against DynamoDB instead of a Painless script.

## Context

ADR-0027 designed CatalogView around a self-hosted OpenSearch domain so it could offer full-text
search and a single atomic Painless script for rating aggregation. That domain was never actually
provisioned: this repository's CDK (`infra/`) had no OpenSearch construct, and
`infra/constructs/appsync-api.ts` referenced two Lambda functions
(`catalogview-get-product`/`catalogview-search-products`) that were never deployed either — the
production SPA build was failing on the missing resolvers before this ADR.

Before writing the missing infra, a cost analysis was done on the three ways to actually run
OpenSearch on AWS:

| Option | Monthly cost (rough) | Why it's a problem here |
|---|---|---|
| Managed OpenSearch domain (single-node `t3.small.search`, free tier expired or N/A) | ~$25–50/mo | A dedicated node running 24/7 for a catalog whose read pattern (a few dozen products, demo traffic) doesn't come close to needing a real search engine. Free tier (ADR-0027 §6) only covers the first 12 months of an AWS account — not a durable answer. |
| Self-hosted OpenSearch on EC2 | ~$3–10/mo (small instance) | Cheapest of the three, but only by giving up things this project otherwise takes seriously: either the instance sits in a public subnet with no real network isolation (weak security posture for a "learning best practices" repo), or it sits private behind a NAT Gateway — and a NAT Gateway alone runs ~$32+/mo before any data-processing charges, erasing the savings and then some. |
| OpenSearch Serverless | ~$175–350/mo floor | Serverless's billing unit (OCUs) has a *minimum* of 2 OCUs for indexing + 2 for search, billed whether or not anything happens — this is a floor, not a variable cost that scales down to near-zero for a low-traffic demo. By far the most expensive option here despite the name. |

All three are rejected. None of them are proportionate to what CatalogView actually needs: a
handful of products, a `contains()`-filtered list, and a rating average — the exact same shape of
problem every other read path in this codebase (`Query.orders.js`, `Query.categories.js`) already
solves with a DynamoDB `Scan` + filter expression.

## Decision

CatalogView becomes **DynamoDB-backed**, consistent with every other service in the repo. It owns
a new table, `catalogview-products` (PK `Id`), populated exactly as before via CDC
(`CatalogProductSyncEvent`, `PriceChangedEvent`, `CatalogCategorySyncEvent`, `ReviewCreatedEvent`,
`ReviewUpdatedEvent`) — only the storage engine underneath `IProductSearchIndex` changes, from
`OpenSearchProductIndex` to `DynamoProductIndex`. The five CatalogView consumer Lambdas
(`catalogview-product-sync-consumer`, `catalogview-review-aggregate-consumer`,
`catalogview-review-update-aggregate-consumer`, `catalogview-price-sync-consumer`,
`catalogview-category-sync-consumer`) are unchanged in trigger/shape — only their persistence
target moves.

`products`/`product` AppSync resolvers move back to **Direct DynamoDB** (reversing ADR-0027 §5):
the "external, non-DynamoDB integration" escalation criterion (ADR-0009 Criterion 3) that justified
Lambda no longer applies once the datastore is DynamoDB again. `GetProduct`/`SearchProducts`
Lambdas, their AppSync Lambda data sources, and their unit tests are deleted.

### Accepted trade-off 1 — no full-text/relevance search

DynamoDB has no query-time relevance ranking or numeric range index over an arbitrary attribute
without a GSI built for that exact purpose. `products(...)` falls back to the same pattern already
used by `Query.orders.js`/`Query.categories.js`: a `Scan` with a `filter` expression —
`contains(#Name, :q) OR contains(#Description, :q)` when `query` is set, `AND`ed with
`#AverageRating BETWEEN :min AND :max` when a rating range is set. `sortBy` becomes **best-effort**:
the resolver sorts only the page of items DynamoDB just returned, not the full result set — there
is no server-side ORDER BY over a Scan, so page 2 is not guaranteed sorted relative to page 1. This
is an explicit, accepted regression from ADR-0027's OpenSearch-backed relevance/sort — proportionate
to a demo catalog, not a production search experience.

### Accepted trade-off 2 — two-step, non-atomic rating aggregation

ADR-0027's entire pitch for OpenSearch was a single atomic Painless script that idempotency-checked,
accumulated, and recomputed the average in one round trip. DynamoDB has no equivalent — `ADD` can
atomically accumulate `RatingSum`/`RatingCount` (with a conditional `ConditionExpression` on
`LastRatingEventId` for idempotency), but the average (`RatingSum / RatingCount`) cannot be computed
inside the same `UpdateExpression`: DynamoDB update expressions can add/subtract/set literals, they
cannot divide two attributes. `ApplyRatingAsync`/`ApplyRatingUpdateAsync` therefore do:

1. A conditional `UpdateItem` — `ADD RatingSum :delta, RatingCount :one SET LastRatingEventId =
   :eventId`, guarded by `ConditionExpression: attribute_not_exists(LastRatingEventId) OR
   LastRatingEventId <> :eventId`. A `ConditionalCheckFailedException` here means the event was
   already applied — caught and treated as a no-op (idempotent replay).
2. A second read (`GetItem`) plus a `UpdateItem` that sets `AverageRating` to the freshly computed
   `RatingSum / RatingCount` (division done in .NET, written back as a literal).

This is **the exact same known limitation ADR-0011 §4 originally had**, before ADR-0027 tried (and,
per this ADR, over-engineered) a fix for it with OpenSearch's atomic script: a crash between step 1
and step 2 leaves `AverageRating` one review stale until the next rating event recomputes it. That
window is accepted here as it was in ADR-0011 — proportionate to a demo project, not a financial
ledger.

## Consequences

### Positive

- **One fewer datastore.** No OpenSearch domain, container, Dashboards UI, or `OpenSearch.Net`
  package to run, patch, or reason about — CatalogView's persistence story matches every other
  service (DynamoDB + CDC).
- **Actually deployable.** `infra/constructs/catalogview-dynamodb.ts` +
  `infra/constructs/catalogview-lambdas.ts` finally give CatalogView a real CDK stack; the SPA build
  no longer references undeployed Lambda functions.
- **Cheaper by a wide margin.** `catalogview-products` on `PAY_PER_REQUEST` billing costs cents/month
  at demo traffic, against the $25–350+/mo floors surveyed above for any OpenSearch option.
- **Simpler local dev.** No `opensearchproject/opensearch`/`opensearch-dashboards` containers, no
  `WithHttpHealthCheck` race to guard against — `catalogview-products` is provisioned the same way
  every other local DynamoDB table is (Aspire `DynamoDBLocalResource` + a seeder's
  `DynamoTableInitializer`).

### Negative / Costs

- **No relevance ranking or true multi-page sort** — accepted trade-off 1 above. A future need for
  real search (typo tolerance, weighted multi-field relevance, faceting) would need to revisit this
  decision; it is not a regression this ADR considers likely for a learning-project catalog.
- **Rating aggregation is non-atomic again** — accepted trade-off 2 above, identical in shape to
  ADR-0011 §4's original cost.
- **`RenameCategoryAsync` is a Scan + N `UpdateItem` calls**, not a single `_update_by_query` — a
  category rename fans out one write per affected product. Acceptable at this catalog's scale; would
  need revisiting (e.g. a GSI on `CategoryIds`, which DynamoDB can't index a list attribute into
  directly, so realistically a denormalization change) if categories affected thousands of products.

### Mitigation Strategies

- Keep the idempotency-guard and non-clobbering-merge invariants (product sync must never
  overwrite Price/Rating fields, mirrored from ADR-0027 §3) covered by unit tests against
  `DynamoProductIndex`, mocking `IAmazonDynamoDB` — same style as
  `Pricing.UnitTests.DataTests.DynamoCampaignRepositoryTests`.
- If real search becomes a requirement later, treat it as a new ADR with its own cost analysis,
  not a silent reintroduction of OpenSearch into this decision's scope.

## References

- [ADR-0027: CatalogView — Product Search and Rating Aggregation via Amazon OpenSearch](./0027-catalogview-opensearch-product-search-and-rating-sync.md) — superseded by this ADR.
- [ADR-0011: Review Bounded Context — Product Ratings Aggregated into Catalog via CDC](./0011-review-bounded-context-rating-aggregation-via-cdc.md) — original two-step DynamoDB aggregation this ADR returns to.
- [ADR-0009: AppSync Resolver Selection — Direct DynamoDB Resolvers as Default](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) — `products`/`product` revert to Direct; the Criterion 3 escalation no longer applies once OpenSearch is gone.
- [ADR-0029: Review Upsert Composite Key and Rating Delta](./0029-review-upsert-composite-key-and-rating-delta.md) — `ApplyRatingUpdateAsync` contract reused unchanged.
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC)](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- `Query.orders.js`/`Query.categories.js` — Scan + `contains()` filter pattern reused by `Query.products.js`.
- `Pricing.UnitTests.DataTests.DynamoCampaignRepositoryTests` — `IAmazonDynamoDB` mocking style reused for `DynamoProductIndexTests`.
