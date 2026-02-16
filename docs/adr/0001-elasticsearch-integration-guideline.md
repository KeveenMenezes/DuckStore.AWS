# ADR-0001: Elasticsearch Integration Guideline

## Status

**Accepted** — February 2026

## Context

DuckStore is a distributed microservices application composed of multiple APIs (Catalog, Basket, Ordering, Discount). Each API interacts with Elasticsearch for search indexing and, indirectly, for observability (APM traces and structured logs via OpenTelemetry).

Without a clear standard, teams risk creating multiple `ElasticsearchClient` instances per API (e.g., one per index or one per concern), leading to:

- Increased DI complexity
- Connection pool fragmentation
- Harder observability integration
- Inconsistent configuration patterns

## Decision

### Single Client Per API

Each API MUST register exactly **one** `ElasticsearchClient` via Dependency Injection using the Aspire integration:

```csharp
builder.AddElasticsearchClient("elasticsearch");
```

Multiple Elasticsearch clients per API are **NOT allowed** unless explicitly justified by infrastructure separation (e.g., completely separate clusters).

### Separation by Index / Data Stream

The single client handles all concerns. Separation happens at the **index** or **data stream** level:

```
ElasticsearchClient
├── index: products          (search index)
├── index: orders            (search index)
├── data-stream: traces-*    (APM / distributed tracing)
└── data-stream: logs-*      (application logs)
```

### Connection Configuration

Connection strings come from standard Aspire configuration, resolved via the `"elasticsearch"` connection name:

```json
{
  "ConnectionStrings": {
    "elasticsearch": "http://elastic:password@localhost:9200"
  }
}
```

In production, this is injected by the Aspire AppHost.

## Responsibilities

### Search Indexes

| Concern | Examples | Rules |
|---------|----------|-------|
| Search | `products`, `orders`, `customers`, `catalog` | Optimized mappings, denormalized documents, read-heavy |

### Observability (APM + Logs)

| Concern | Data Streams | Rules |
|---------|-------------|-------|
| Traces  | `traces-*`  | Handled by OpenTelemetry + Elastic APM — APIs must NOT manually write traces |
| Logs    | `logs-*`    | Structured JSON, `TraceId` must flow across services. Uses daily rollover (`logs-{service}-{date}`) |

**Index Lifecycle Management (ILM):**
Data streams automatically rollover daily (e.g., `logs-basket-api-2026.02.15`, `logs-basket-api-2026.02.16`). ILM policies automatically delete old logs to prevent excessive disk usage. See [ADR-0002: Elasticsearch Index Lifecycle Management](./0002-elasticsearch-index-lifecycle-management.md) for retention policies.

### Analytics (Optional)

| Concern | Examples | Rules |
|---------|----------|-------|
| Events  | `orders-events`, `checkout-events`, `payments-events` | Write-only from application services or consumers |

## Implementation Pattern

### Correct

```csharp
// Program.cs — register once
builder.AddElasticsearchClient("elasticsearch");

// Any service — inject the single client
public class ProductSearchService(ElasticsearchClient client)
{
    // Use client with the appropriate index
}
```

### Incorrect

```csharp
// DO NOT create multiple clients for different indexes
builder.AddKeyedElasticsearchClient("products");
builder.AddKeyedElasticsearchClient("orders");
```

## Consequences

- **Reduced DI complexity** — one client registration per API
- **Simplified observability** — single connection for all telemetry
- **Aligned with Elastic production patterns** — index-level separation, not client-level
- **Centralized connection management** — configuration in one place
- **No unnecessary client duplication** — shared connection pool
- **Automatic data lifecycle** — ILM policies manage index retention and cleanup

## Applies To

All DuckStore APIs:

- Catalog.API
- Basket.API
- Ordering.API
- Discount.Grpc

## Related Documentation

- [ADR-0002: Elasticsearch Index Lifecycle Management](./0002-elasticsearch-index-lifecycle-management.md) — Data retention and lifecycle policies
