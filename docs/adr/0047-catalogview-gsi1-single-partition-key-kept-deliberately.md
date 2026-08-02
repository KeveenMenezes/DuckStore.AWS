---
tags:
  - status/proposed
  - domain/catalogview
---

# ADR-0047: CatalogView's GSI1 Keeps a Single Partition Key Value — Deliberately

## Status
**Proposed** — August 2026

Documents a property of the index introduced by
[ADR-0030](./0030-catalogview-dynamodb-drop-opensearch.md), which chose the DynamoDB-backed
`catalogview-products` table but never recorded the cost of its browse index. It does not supersede
ADR-0030 — the storage decision stands unchanged. It adds the binding constraints that keep that
decision viable (Decision §4 and §5).

---

## Context

`catalogview-products` carries a GSI whose partition key is the **constant string `"PRODUCT"`**, with
`GSI1SK = AverageRating` as the sort key:

```ts
// infra/constructs/catalogview-dynamodb.ts
this.catalogViewProductsTable.addGlobalSecondaryIndex({
  indexName: 'GSI1',
  partitionKey: { name: 'GSI1PK', type: dynamodb.AttributeType.STRING },  // always "PRODUCT"
  sortKey: { name: 'GSI1SK', type: dynamodb.AttributeType.NUMBER },       // AverageRating
  projectionType: dynamodb.ProjectionType.ALL,
});
```

It exists so the storefront's default browse can `Query` instead of `Scan`. Every product in the
catalog therefore shares one partition key value, and that one value is on both hot paths:

| Path | Operation | Where |
|---|---|---|
| Browse / rating filter | `Query GSI1PK = "PRODUCT" AND GSI1SK BETWEEN :min AND :max` | `graphql/resolvers/products/queries/Query.products.js` |
| Product created or edited | `UpdateItem` setting `GSI1PK` + `GSI1SK` | `DynamoProductIndex.UpsertAsync` |
| Any review created or edited | `UpdateItem` setting `GSI1SK` | `DynamoProductIndex.RecomputeAverageAsync` |

"One partition key for the whole catalog" reads like a textbook hot-partition defect, and the
textbook fix is write sharding — `GSI1PK = "PRODUCT#<0..N>"` with N parallel queries merged at read
time. That fix was analyzed and **rejected**. Two findings drove the rejection.

### Finding 1 — sharding cannot improve read throughput for this query

A globally rating-ordered page of `L` items cannot be assembled from fewer than all `N` shards: any
shard may hold all `L` top-rated products, so each shard must be queried with limit `L` and the
results merged. Per-partition cost per request is therefore unchanged, and the added partitions are
consumed exactly by the fan-out:

| | partitions | items read per request | items read **per partition** | ceiling |
|---|---|---|---|---|
| Today (`"PRODUCT"`) | 1 × 3,000 RCU | L | L | 3,000 RCU ÷ L·itemRCU |
| Sharded (N shards) | N × 3,000 RCU | N·L | L | 3,000 RCU ÷ L·itemRCU |

The request-rate ceiling is **identical**, while total RCU consumed per request rises by N×. This is
why AWS names the pattern *write* sharding: it raises the write and storage ceilings, never the read
ceiling of a query that must span every shard.

### Finding 2 — split for heat already applies here

Adaptive capacity's "split for heat" can spread items sharing one partition key value across
multiple physical partitions, and
[it works on GSIs, not just base tables](https://aws.amazon.com/blogs/database/part-3-scaling-dynamodb-how-partitions-hot-keys-and-split-for-heat-impact-performance/).
Two documented conditions disable it, and **neither holds** for this table:

- **An LSI on the table.** `catalogview-products` has none — only this GSI. (An LSI prevents partition
  splits within an item collection entirely.)
- **A monotonic sort key.** Split for heat is skipped when the sort key increases or decreases
  monotonically, because no cut point helps if every new write lands past it. `GSI1SK` is a bounded
  rating in `[0, 5]` that moves both directions as reviews arrive — not monotonic.

So the ceiling is softer than the naive reading suggests: the per-partition quota is
**3,000 RCU / 1,000 WCU**, but `"PRODUCT"` is not permanently confined to one partition. Split for
heat is best-effort, takes minutes to react, and is explicitly an implementation detail — it is a
mitigation to rely on knowingly, not a guarantee to design against.

### What actually binds first, quantified

Reads, on a GSI, are eventually consistent: 0.5 RCU per 4 KB. A product document here is roughly
1–2 KB, so a 20-item browse page costs ~2.5–5 RCU. Against a 3,000 RCU partition quota that is
**≈600–1,200 browse requests per second** before a single partition throttles — and only if split for
heat never fires.

Writes are the tighter of the two: 1 WCU per 1 KB, so the 1,000 WCU quota is on the order of
**several hundred product edits plus review events per second, sustained**. A write-throttled GSI
also applies back pressure to writes on the base table, which makes it the more damaging of the two
ceilings.

Both numbers are orders of magnitude beyond this project's traffic. Neither justifies a scatter-gather
read path today, and one of them cannot be improved by sharding at all.

---

## Decision

### 1. `GSI1PK` stays the constant `"PRODUCT"`

The single partition key value is **accepted and deliberate**, not an oversight. Its cost is bounded
by the quantification above and its benefit is that the browse path stays a single `Query` behind a
single AppSync direct resolver ([ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)).

### 2. Write sharding of GSI1 is NOT ALLOWED as a read-performance change

Any proposal to shard `GSI1PK` MUST state which ceiling it targets. Sharding to make browse *reads*
faster or more scalable is rejected by Finding 1 — it raises cost N× and moves the read ceiling by
zero. Only a demonstrated **write** or **storage** ceiling justifies revisiting it.

### 3. Read scaling, when it is ever needed, is answered by caching — not by index shape

The browse response contains no per-user data, so identical pages are served to every visitor. The
proportionate escalation is AppSync server-side caching keyed on the field arguments, not a change to
the index. It is deliberately **not** enabled now: an AppSync cache cluster runs ~US$32/month, the
same order of magnitude as the managed OpenSearch domain ADR-0030 rejected on cost. Enabling it
requires an ADR that accepts that recurring cost explicitly.

### 4. `GSI1SK` MUST NOT become monotonic

This is the load-bearing constraint of the whole decision. Redefining `GSI1SK` to a value that only
increases or decreases — a timestamp, a sequence number, a "newest first" ordinal, a
`CreatedAt`-derived sort — would disable split for heat for this index and hard-cap the entire
catalog's index writes at 1,000 WCU, with back pressure onto the base table.

**Correct** — a bounded, non-monotonic rating; splits remain possible:

```csharp
// DynamoProductIndex.RecomputeAverageAsync
UpdateExpression = "SET AverageRating = :average, GSI1SK = :average"
```

**Incorrect** — a "sort by newest" index reusing GSI1 with a monotonic key:

```csharp
// ⛔ Every write lands past any cut point: split for heat never helps, and the GSI
//    throttles the base table's writes with it.
UpdateExpression = "SET GSI1SK = :updatedAtEpoch"
```

A browse ordering that genuinely needs a monotonic sort key MUST use its own index and MUST carry a
sharded or naturally high-cardinality partition key — it MUST NOT be bolted onto GSI1.

### 5. `catalogview-products` MUST NOT gain a local secondary index

An LSI prevents partition splits within an item collection entirely, which would convert the soft
ceiling described in Finding 2 into a hard one. Any additional access pattern on this table uses a
GSI, even where an LSI would otherwise fit.

### 6. Review triggers

This decision is revisited when any of the following becomes true — not on a schedule:

| Trigger | Signal to watch | Fix that applies |
|---|---|---|
| Sustained index write pressure | `WriteThrottleEvents` on GSI1, or throttled base-table writes with no base-table cause | Shard `GSI1PK` (Finding 1 does not apply to writes) |
| Sustained browse read pressure | `ReadThrottleEvents` on GSI1 | AppSync caching (§3) — sharding is not the answer |
| Catalog outgrows the browse model | Free-text search or relevance ranking becomes a product requirement | Reopen ADR-0030's search-engine analysis; sharding is irrelevant to it |

---

## Consequences

### Positive

- The browse path stays one `Query` on one direct resolver: no Lambda, no cold start, no fan-out, and
  1× read cost instead of N×.
- The index's real ceilings are now written down with the arithmetic behind them, so the next person
  to look at `GSI1PK = "PRODUCT"` reaches a conclusion in minutes instead of re-deriving it — or
  "fixing" it into something 10× more expensive.
- §4 and §5 protect the mechanism (split for heat) the decision depends on. Both are the kind of
  change that would otherwise be made casually, in an unrelated PR, with no visible symptom until
  production write throttling.

### Negative / Costs

- The read ceiling is real, whatever its magnitude: a single partition key value means the browse path
  has a throughput bound the rest of the table does not.
- Split for heat is best-effort, undocumented in its exact triggers, and takes minutes to react. A
  sudden traffic spike can throttle before it adapts; burst capacity is the only cushion.
- The decision rests on a traffic assumption. If DuckStore's usage profile ever changes materially,
  the analysis must be re-run rather than assumed to still hold.

### Mitigation Strategies

- The triggers in §6 are CloudWatch metrics that already exist on the table (`ReadThrottleEvents` /
  `WriteThrottleEvents` per index); no new instrumentation is required to detect the conditions that
  invalidate this ADR.
- `PAY_PER_REQUEST` billing means the table absorbs spikes up to double the previous peak without
  provisioning work, which covers the window split for heat needs to react.

### Future Constraints

- Any new index on `catalogview-products` MUST declare its partition key cardinality and whether its
  sort key is monotonic, and MUST NOT reuse GSI1 for a second ordering.
- Enabling AppSync caching (§3) requires its own ADR accepting the recurring cost, because it reverses
  the cost reasoning ADR-0030 used to reject a search engine.

---

## Applies To

- `src/Services/CatalogView/CatalogView.Function` — `Modules/Products/Data/DynamoProductIndex.cs`
  (`UpsertAsync`, `RecomputeAverageAsync`, `ToItem`)
- `infra/constructs/catalogview-dynamodb.ts` — the GSI1 definition
- `graphql/resolvers/products/queries/Query.products.js` — the browse branch

---

## References

- [ADR-0009: AppSync Resolver Selection — Direct First, Lambda for Complex Logic](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0027: CatalogView OpenSearch Product Search and Rating Sync](./0027-catalogview-opensearch-product-search-and-rating-sync.md)
- [ADR-0030: CatalogView Goes DynamoDB-Backed — Drop OpenSearch](./0030-catalogview-dynamodb-drop-opensearch.md)
- [DynamoDB burst and adaptive capacity](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/burst-adaptive-capacity.html) — per-partition 3,000 RCU / 1,000 WCU; isolating frequently accessed items; the LSI exception
- [Scaling DynamoDB: partitions, hot keys, and split for heat (Part 3)](https://aws.amazon.com/blogs/database/part-3-scaling-dynamodb-how-partitions-hot-keys-and-split-for-heat-impact-performance/) — split for heat applies to GSIs; the monotonic-sort-key exception and its base-table back pressure
- [Using write sharding to distribute workloads evenly](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-partition-key-sharding.html)
