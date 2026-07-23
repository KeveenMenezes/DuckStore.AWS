# ADR-0039: Order Analytics Pipeline via EventBridge, Kinesis Data Firehose, and an S3 Data Lake

## Status
**Proposed** — July 2026

---

## Context

Ordering already has a full CDC pipeline for integration events: DynamoDB Streams
(`StreamViewType.NEW_AND_OLD_IMAGES` on the `ordering` table, see
`infra/constructs/ordering-dynamodb.ts`) → `OrderStreamPublisherFunction` →
`StreamRuleDispatcher<OrderStreamImage>` → `OrderCreatedRule` (matches `INSERT` +
`Type=="Order"`) → an `OrderCreatedEvent` published to EventBridge, carrying `CustomerId`,
`OrderName`, `Status`, the full `ShippingAddress`, the full `Payment`, and `Items`
(`ProductId`, `Quantity`, `Price`).

There is no analytical layer on top of this data today. Questions like "revenue by month",
"best-selling products", or "average time from `Pending` to `Completed`" cannot be answered
without querying the live `ordering` table directly — and that table is a single item per
order with `OrderItems` embedded as a list, shaped for `GetItem`-by-id and
`Query`-by-customer-via-GSI1 access patterns, not for aggregation.

Three architectural options were evaluated for closing this gap:

| Option | Shape | Latency | Approx. cost (this project's low volume) |
|---|---|---|---|
| **A** | EventBridge → Kinesis Data Firehose → S3 (Parquet) → Glue Catalog → Athena / QuickSight | ~15 min (batch) | ~$1–10/month |
| **B** | Kinesis Data Streams → Managed Service for Apache Flink → OpenSearch | seconds (streaming) | ~$120–800+/month |
| **C** | Redshift Serverless / Redshift Spectrum over the same S3 lake | seconds–minutes, query-dependent | ~$20–100/month |

A fourth option — using **Athena Federated Query** (the DynamoDB connector) directly
against the live `ordering` table, skipping a data lake entirely — was also considered and
rejected; see "Rejected Alternatives" below.

### Why cost differs so much between options

Option A is pure consumption-based pricing (Firehose per-GB, S3 per-GB-month, Athena per-TB-scanned,
a few minutes of Glue Crawler DPU-time) — there is no reserved capacity sitting idle. Option B's
cost floor is structural, not volume-driven: Managed Service for Apache Flink bills a minimum of
~1 KPU-hour (~$80/month) and OpenSearch Serverless has a 4-OCU minimum (~$0.24/OCU-hour ≈ $700+/month)
**regardless of how many events actually flow through them**. Option C sits in between — Redshift
Serverless bills by active RPU-hour with an 8-RPU minimum configuration, so cost tracks usage more
than B does, but it is still a compute floor Option A does not have.

These are rough, region-dependent (us-east-1), list-price estimates for orientation only — validate
against the AWS Pricing Calculator before committing budget.

---

## Decision

Adopt **Option A** — EventBridge → Kinesis Data Firehose → S3 (Parquet) → Glue Data Catalog →
Athena / QuickSight — as the analytics architecture for Order data.

### 1. Ingestion reuses the existing `OrderCreatedEvent` — no new Streams consumer

A new EventBridge rule filters on `detail-type == "OrderCreatedEvent"` and targets a Kinesis
Data Firehose delivery stream **directly** — EventBridge supports Firehose as a native target,
so no intermediary Lambda is needed purely for routing. No new DynamoDB Streams consumer is
introduced on the `ordering` table; the analytics pipeline taps the integration event that
already exists.

A follow-up (not required for this ADR to be actionable, but a known next step) is an
`OrderAnalyticsRule` alongside `OrderCreatedRule` in the same `StreamRuleDispatcher`, to also
capture status transitions (`Pending` → `Completed` → etc.) as a distinct event type, since the
table's `NEW_AND_OLD_IMAGES` stream view already carries the old/new status needed to detect
that transition. Until that rule exists, this pipeline only sees order creation, not its
lifecycle.

### 2. Enrichment happens in Firehose's own transform Lambda — never via a synchronous cross-service call

Firehose's data-transformation Lambda performs only cheap, in-line enrichment derivable from
the event itself, with no outbound calls to other services or tables:

- Derive `year`/`month`/`day` partition keys from the event's `CreatedAt` (business date).
- Convert `Status` and `PaymentMethod` from their integer enum values to readable strings.
- Compute `TotalPrice` (a derived, non-persisted property on the `Order` aggregate —
  `Order.cs:116`) since the raw event does not carry it as a stored field.
- Flatten `Items` into one row per order line, producing a granular fact grain
  (`fct_order_items`) rather than one JSON blob per order.

Firehose then converts the transformed records to Parquet via a registered Glue schema.

**Cross-domain enrichment (product name/category, customer name) is explicitly NOT done here.**
Doing so would require the transform Lambda to call out to Catalog/User synchronously per
record, recreating the exact OLTP-coupling problem this ADR rejects for the DynamoDB-federated
approach (see below) — just against a different service's table instead of Ordering's. Fact
tables stay keyed only by foreign IDs (`ProductId`, `CustomerId`); see §6.

### 3. File format: Parquet + Snappy

Parquet is chosen over JSON/CSV: columnar storage lets Athena prune unread columns, row-group
statistics enable predicate pushdown (e.g. skip blocks outside a queried date range), and the
format is splittable for parallel scan — all of which directly reduce Athena's
per-TB-scanned cost. Snappy is used for its decompression speed, which matters more than
compression ratio when the query bottleneck is I/O, not storage.

### 4. Partitioning: by business date, not by arrival time

Partitions (`year=/month=/day=`) are derived from the order's `CreatedAt` via Firehose's
dynamic partitioning (JQ expression over the event payload), not from Firehose's ingestion/
arrival timestamp. This avoids a late-arriving event (e.g. one delayed past midnight) landing
in the wrong day's partition and skewing day-level aggregates.

### 5. Buffering: maximum buffer hints, small files are an accepted reality at this volume

Firehose buffer hints are set to the maximum allowed: `128 MB` or `900s` (15 minutes),
whichever comes first.

At this project's demo/low-volume traffic, the 15-minute timeout will almost always fire
before the 128 MB size threshold, producing small Parquet files. **This is expected physical
behavior, not a misconfiguration** — maximizing the buffer window is still the correct setting,
because it minimizes file count as much as is achievable pre-compaction; it just cannot
eliminate the small-files effect at low volume by itself. §6 covers the mitigation.

### 6. Daily compaction closes the small-files gap

A separate Glue ETL job, scheduled once daily (~02:00, off-peak), reads the **previous day's
now-closed partition**, merges its small Parquet files into a single larger file per partition,
and replaces the small files. It never touches the current/open day's partition, because that
partition may still receive late-arriving events.

### 7. Partition and schema registration

Firehose's dynamic partitioning + metadata extraction registers new partitions directly in the
Glue Data Catalog at write time — **no recurring Glue Crawler is used for partition discovery**;
that would burn DPU-hours rediscovering partitions Firehose already knows about.

A separate, lightweight Glue Crawler runs once daily (~03:00, off-peak) with a narrower purpose:
detecting **schema evolution** (a new field added to the event) — not partition discovery.

### 8. Cross-domain data is modeled as a star schema, joined at query time

`fct_orders` / `fct_order_items` stay lean, carrying only foreign IDs (`ProductId`,
`CustomerId`). Dimension tables — `dim_product` fed by Catalog's own existing CDC/
stream-publisher pipeline, `dim_customer` similarly from User's CDC if/when needed — are built
and refreshed independently by each owning domain, through the same Firehose → S3 pattern
described here, each on its own cadence. Cross-domain enrichment happens via SQL `JOIN` in
Athena/QuickSight at query time, not during ingestion.

This mirrors the project's existing CDC philosophy — each service owns and publishes its own
data — applied to the analytics layer instead of the operational one. `dim_product` is a
dependency this ADR notes but does not itself implement; it is a candidate for a companion ADR
once Catalog's side of the work is scoped.

### 9. Consumption: Athena for ad-hoc SQL, QuickSight for dashboards

Amazon Athena serves ad-hoc analytical SQL — partition and columnar pruning keep its
per-TB-scanned cost negligible at this project's volume. Amazon QuickSight serves dashboards,
either querying Athena directly (always fresh, slower to render) or through SPICE with a
scheduled refresh (e.g. daily). This is a freshness-vs-render-speed trade-off decided per
dashboard, not mandated globally by this ADR.

### 10. Latency expectation

Data is queryable in Athena within **~15 minutes** of the order event (one Firehose buffer
flush) and compacted/optimized within **~24 hours** (the next day's compaction job). This is
explicitly acceptable for a BI use case and is not intended to support an operational or
real-time requirement.

---

## Rejected Alternatives

### Option B — Kinesis Data Streams + Managed Service for Apache Flink + OpenSearch

Rejected primarily on cost structure: Managed Service for Apache Flink carries a fixed floor
of roughly one KPU-hour (~$80/month) and OpenSearch Serverless a 4-OCU minimum (~$700+/month),
**both nearly independent of this project's actual event volume** — versus Option A's
consumption-only pricing with no reserved capacity. Streaming windowing constructs (tumbling,
hopping/sliding, session, global/unbounded windows) are a Flink/stream-processing concept that
has no equivalent need in Option A's batch model; introducing them now would be unjustified
complexity. This remains a documented future evolution path, not a rejected-forever option — it
becomes relevant only if a genuine near-real-time requirement (e.g. a live operational
dashboard) emerges later.

### Option C — Redshift Serverless as the primary analytical engine

Rejected as the *default* choice: it introduces a moderate fixed compute floor (RPU-hours)
without Option B's near-real-time benefit to justify it. Kept as a documented future evolution
path — specifically for the point at which cross-domain analytical joins (Order + Catalog +
Review + Pricing) outgrow what federated Athena queries over the S3 lake can comfortably serve
(e.g. Redshift Spectrum reading the same S3 lake as an alternative query engine, without
re-ingesting data).

### Athena Federated Query (DynamoDB connector) directly against the live `ordering` table

Explicitly rejected as a substitute for a data lake, for five compounding reasons:

1. **OLTP/OLAP coupling.** The federated connector performs a live `Scan`/`Query` against the
   production table at query time, consuming RCU and competing with real checkout/order
   traffic — reintroducing exactly the coupling that [ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)'s
   CDC model exists to prevent, just at the analytics layer instead of the integration layer.
2. **No columnar pruning.** DynamoDB is not columnar; an aggregate query (`SUM`, `GROUP BY`)
   requires a full-table scan with no partition or column pruning, unlike Parquet's row-group
   statistics and predicate pushdown.
3. **Data-shape mismatch.** `ordering`'s single-item-with-embedded-`OrderItems`-list design
   (per [ADR-0010](./0010-collapse-ordering-into-single-function-single-item-model.md)) is
   shaped for OLTP access patterns, not flat analytical rows; the federated connector has
   limited support for translating nested list attributes into SQL rows, whereas
   `OrderCreatedEvent` already provides a flat `Items` array for free.
4. **Current-state only.** DynamoDB reflects only the latest state; a federated query cannot
   recover historical transitions (e.g. time spent in `Pending` before `Completed`) that an
   immutable, event-sourced S3 lake preserves by construction.
5. **No caching.** Every federated query re-scans the live table; repeated dashboard queries
   against the same data pay the same Scan cost each time, unlike cheap, repeatable Parquet
   reads against static files in S3.

---

## Applies To

- `infra/` (CDK) — new constructs for: the EventBridge rule targeting Firehose, the Firehose
  delivery stream and its transform Lambda, the Glue Data Catalog database/tables, the daily
  compaction Glue job, the daily schema-detection Glue Crawler, the destination S3 bucket.
- `src/Services/Ordering/Ordering.Function` — no code change required to produce
  `OrderCreatedEvent`; only the follow-up `OrderAnalyticsRule` (§1) touches
  `EventsIntegration/Publishers/Rules/`.
- Future companion work in `src/Services/Catalog/Catalog.Function` — a `dim_product` feed
  (§8), out of scope for this ADR.

---

## Consequences

### Positive

- Reuses the existing CDC/EventBridge investment — no new DynamoDB Streams consumer on
  `ordering`, no duplicate integration-event machinery.
- Near-zero idle cost, consistent with the project's AWS-first/pay-per-use philosophy already
  established for EventBridge in [ADR-0004](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md).
- Fully decoupled from OLTP performance — no analytics query can throttle or slow down the
  live checkout/order path.
- Parquet + partitioning keeps Athena's per-TB-scanned cost negligible at this project's volume.
- The star-schema/dimension-table approach keeps analytics ingestion resilient to other
  services' availability — Ordering's fact pipeline never blocks on Catalog or User being up.

### Negative / Costs

- A ~15-minute latency floor exists between an order event and its availability in Athena — not
  suitable if a future requirement demands true real-time visibility.
- At this project's demo volume, Parquet files will be small until the daily compaction job
  runs — documented as expected behavior given the physics of the Firehose buffer, not a defect
  to "fix" by further buffer tuning.
- Introduces new infrastructure surface that must be provisioned and maintained in `infra/`
  like everything else in this project: a Firehose delivery stream, its transform Lambda, a
  daily compaction Glue job, a daily schema-detection Glue Crawler, and Glue Catalog
  tables/databases.
- Cross-domain enrichment (`dim_product`, `dim_customer`) depends on the owning service
  (Catalog, User) adopting the same Firehose → S3 pattern for its own dimension feed. This ADR
  mandates the pattern only for Order's own fact tables; `dim_product` is noted as a dependency
  and a candidate for a future companion ADR, not committed to here.

### Mitigation Strategies

- Keep this ADR's status as **Proposed** (not Accepted) until `dim_product`'s owning-service
  work and the concrete CDK infra are scoped — accepting it prematurely would commit to a
  pipeline whose dimension side is still undefined.
- Treat the daily compaction job and the daily schema-detection Crawler as the reference
  pattern to replicate if/when other services (Catalog, Review) build their own analytics fact/
  dimension tables, rather than each service inventing its own compaction cadence.
- If a genuine near-real-time requirement emerges later, revisit Option B rather than trying to
  force sub-minute latency out of this batch design by shrinking the Firehose buffer window
  below its practical minimum.

---

## References

- [ADR-0004: AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0010: Collapse Ordering into a Single Function, Single-Item Model](./0010-collapse-ordering-into-single-function-single-item-model.md)
- [ADR-0015: SQS Dead-Letter Queues for CDC Publisher Lambdas and EventBridge Consumer Retry Policy](./0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md)
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
- [ADR-0021: Fail-Fast EventBridge Publishing on AWS](./0021-fail-fast-eventbridge-publishing-on-aws.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- `src/Services/Ordering/Ordering.Function/Modules/Orders/EventsIntegration/Publishers/Rules/OrderCreatedRule.cs`
- `infra/constructs/ordering-dynamodb.ts`, `infra/constructs/ordering-lambdas.ts`
