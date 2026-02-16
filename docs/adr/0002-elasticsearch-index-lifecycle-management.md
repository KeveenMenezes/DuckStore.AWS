# ADR-0002: Elasticsearch Index Lifecycle Management (ILM)

## Status

**Accepted** — February 2026

## Context

DuckStore uses Elasticsearch for observability (logs and traces via OpenTelemetry) and search indexing across multiple microservices. Without proper data lifecycle management, the following problems occur:

- **Unbounded index growth** — Logs accumulate indefinitely, consuming disk space
- **Performance degradation** — Large indices slow down queries and indexing operations
- **Resource exhaustion** — Elasticsearch clusters can run out of disk space, causing failures
- **Manual intervention required** — Teams must manually delete old indices periodically

Data streams for logs follow a daily rollover pattern:
- `logs-basket-api-2026.02.15`
- `logs-catalog-api-2026.02.16`
- `logs-ordering-api-2026.02.17`

This pattern is correct and enables efficient time-based queries, but requires lifecycle policies to prevent indefinite accumulation.

## Decision

### ILM Policy for Logs: 30-Day Retention

All log data streams MUST use an Index Lifecycle Management (ILM) policy with the following characteristics:

**Policy Name:** `logs-30day-retention`

**Phases:**

1. **Hot Phase** (indexing + querying)
   - Rollover after 1 day OR when primary shard reaches 50GB
   - Priority: 100 (high)
   
2. **Delete Phase** (after 30 days)
   - Automatically delete indices older than 30 days
   - No archival or snapshot required

### Index Template Pattern

The ILM policy MUST be applied via an index template matching all log data streams:

**Template Name:** `logs-duckstore-template`  
**Index Pattern:** `logs-*-api-*`  
**Priority:** 500

This ensures all new log data streams automatically inherit the lifecycle policy.

### Implementation

```json
PUT _ilm/policy/logs-30day-retention
{
  "policy": {
    "phases": {
      "hot": {
        "min_age": "0ms",
        "actions": {
          "rollover": {
            "max_age": "1d",
            "max_primary_shard_size": "50gb"
          },
          "set_priority": {
            "priority": 100
          }
        }
      },
      "delete": {
        "min_age": "30d",
        "actions": {
          "delete": {}
        }
      }
    }
  }
}

PUT _index_template/logs-duckstore-template
{
  "index_patterns": ["logs-*-api-*"],
  "data_stream": {},
  "template": {
    "settings": {
      "index.lifecycle.name": "logs-30day-retention",
      "index.lifecycle.rollover_alias": "logs",
      "number_of_shards": 1,
      "number_of_replicas": 0
    }
  },
  "priority": 500
}
```

### Environment-Specific Policies

Different retention periods MAY be used per environment:

| Environment | Retention | Policy Name | Rationale |
|-------------|-----------|-------------|-----------|
| Development | 7 days | `logs-7day-retention` | Faster cleanup, limited resources |
| Staging | 14 days | `logs-14day-retention` | Balance between cost and debugging needs |
| Production | 30 days | `logs-30day-retention` | Compliance and troubleshooting requirements |

### Exclusions

The following data streams are **NOT** subject to automatic deletion:

- **Traces** (`traces-*`) — Use OpenTelemetry Collector retention settings instead
- **Search indices** (`products`, `orders`, `catalog`) — Business data, retained indefinitely
- **Event streams** (`*-events`) — May require longer retention for analytics (configure separately)

## Monitoring and Verification

### Required Checks

Teams MUST verify ILM policy application:

```json
# Verify policy exists
GET _ilm/policy/logs-30day-retention

# Check which indices are managed
GET logs-*/_ilm/explain

# View data stream status
GET _data_stream/logs-*

# Monitor index sizes
GET _cat/indices/logs-*?v&s=index&h=index,store.size,docs.count,creation.date.string
```

### Alerts

Set up monitoring alerts for:
- ILM policy execution failures
- Disk usage above 80%
- Indices not rolling over as expected

## Consequences

### Positive

- **Automatic cleanup** — No manual intervention required to delete old logs
- **Predictable disk usage** — Maximum of 30 days of logs per service
- **Performance maintained** — Indices stay within optimal size limits
- **Cost efficiency** — Prevents unbounded storage growth
- **Operational simplicity** — Set-and-forget configuration

### Negative

- **Data loss after retention period** — Logs older than 30 days are permanently deleted
- **No long-term analytics** — Historical data beyond 30 days unavailable unless exported
- **Initial setup required** — Must apply policies to existing indices

### Mitigation Strategies

If longer retention is needed:
1. **Archive to object storage** — Use snapshot repository (S3, GCS, Azure Blob)
2. **Export to data warehouse** — Stream logs to BigQuery, Snowflake, etc.
3. **Adjust retention period** — Increase to 60/90 days based on compliance requirements

## Applies To

All DuckStore microservices generating logs:

- Catalog.API
- Basket.API
- Ordering.API
- Discount.Grpc
- YarpApiGateway

## Related Documentation

- [ADR-0001: Elasticsearch Integration Guideline](./0001-elasticsearch-integration-guideline.md)
- [Elasticsearch ILM Documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/index-lifecycle-management.html)
- [Data Streams Best Practices](https://www.elastic.co/guide/en/elasticsearch/reference/current/data-streams.html)

## References

- Elastic ILM Best Practices
- OpenTelemetry Logging Specification
- DuckStore Observability Requirements
