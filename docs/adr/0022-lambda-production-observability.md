# ADR-0022: Production Observability for Lambda Functions — BuildingBlocks.ServiceDefaults.Lambda

## Status
**Accepted** — July 2026

---

## Context

Observability in this project is inverted: the **local** environment is rich, and **production has
almost nothing**.

- Locally, Aspire (`src/AppHost/ObservabilityExtensions.cs`) provisions Elasticsearch + Kibana, and
  `BuildingBlocks.ServiceDefaults/Extensions.cs` (`AddServiceDefaults()`) wires Serilog →
  Elasticsearch plus OpenTelemetry traces/metrics/logs, per
  [ADR-0001](./0001-elasticsearch-integration-guideline.md) /
  [ADR-0002](./0002-elasticsearch-index-lifecycle-management.md).
- But `AddServiceDefaults()` is only called from the `*.DevelopmentDataSeeder` worker projects.
  **No `*.Function` Lambda startup uses any of it** — the `[LambdaStartup]` classes register at
  most `services.AddLogging()`. On AWS, the Lambdas emit only the runtime's default CloudWatch
  output: no structured properties, no correlation, no traces, no metrics.
- AppSync has `xrayEnabled: true` (`infra/constructs/appsync-api.ts`), so a trace starts at the
  GraphQL edge and **dead-ends at the Lambda boundary** — the most interesting hops (Lambda →
  DynamoDB, Streams publisher → EventBridge, EventBridge → consumer) are invisible.
- `CLAUDE.md` documents a `BuildingBlocks.ServiceDefaults.Lambda` project ("Lambda hosting
  equivalents of the above") that **does not exist** in the repository — the intended solution was
  named but never built.

The full `AddServiceDefaults()` is not the answer for Lambdas: it assumes a long-lived host
(service discovery, health-check endpoints, an Elasticsearch sink reachable on a local network),
which either doesn't apply to Lambda or adds cold-start weight and a network dependency that AWS
already covers natively with CloudWatch and X-Ray.

---

## Decision

Create the missing **`BuildingBlocks.ServiceDefaults.Lambda`** project and make every Lambda
`[LambdaStartup]` call it. It is the Lambda-shaped subset of ServiceDefaults: structured logging to
CloudWatch and distributed tracing via X-Ray/OpenTelemetry — no Elasticsearch, no Kibana, no health
endpoints, no service discovery.

### 1. One extension, called by every Lambda startup

```csharp
// BuildingBlocks.ServiceDefaults.Lambda
public static IServiceCollection AddLambdaDefaults(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddLambdaStructuredLogging(configuration); // §2
    services.AddLambdaTracing(configuration);           // §3
    return services;
}
```

Every `*.Function/Startup.cs` (Basket, Catalog, Ordering, Review, User) MUST call
`services.AddLambdaDefaults(configuration)` as its first registration. New Lambda projects are not
compliant without it.

### 2. Structured logging — JSON to stdout, CloudWatch as the sink

- Logs are written as **single-line JSON to stdout** (CloudWatch Logs ingests stdout natively);
  there is no push-to-Elasticsearch from Lambda. ADR-0001/0002 remain the standard for the local
  Aspire environment; CloudWatch Logs is the production log store.
- Every log line carries at minimum: timestamp, level, message template + rendered properties,
  `AwsRequestId`, function name/version (from `AWS_LAMBDA_FUNCTION_NAME`/`_VERSION`), and the
  trace id when available — so CloudWatch Logs Insights can query by property instead of grepping
  text.
- Implementation uses `Microsoft.Extensions.Logging` with a JSON console formatter (or
  `Amazon.Lambda.Logging.AspNetCore`'s structured mode) — the choice is an implementation detail;
  the binding constraint is **JSON-structured, correlation-carrying stdout logs**.
- Locally under Aspire, the same abstraction is active; log output remains readable in the Aspire
  dashboard. The Elasticsearch sink stays where it is today (ServiceDefaults for hosted workers),
  it is NOT pulled into the Lambda package.

### 3. Tracing — X-Ray so AppSync traces stop dead-ending

- Active tracing is enabled on the Lambda functions in `infra/` (CDK `tracing: lambda.Tracing.ACTIVE`),
  continuing the trace AppSync already starts.
- The AWS SDK clients (`IAmazonDynamoDB`, `IAmazonEventBridge`) are instrumented (AWSSDK X-Ray
  handler or OpenTelemetry with the X-Ray exporter/ADOT layer) so DynamoDB calls and EventBridge
  publishes appear as subsegments. Which of the two instrumentation routes to use is decided at
  implementation time; the binding constraint is **AppSync → Lambda → DynamoDB/EventBridge appears
  as one trace**.
- Tracing is a no-op locally (no X-Ray daemon under Aspire) — the registration MUST tolerate the
  daemon's absence silently.

### 4. Documentation correction

`CLAUDE.md`'s description of `BuildingBlocks.ServiceDefaults.Lambda` becomes true instead of being
deleted: the project is created with the scope above. Central package versions go to
`Directory.Packages.props` as usual.

---

## Applies To

- New project: `src/BuildingBlocks/BuildingBlocks.ServiceDefaults.Lambda`
- `src/Services/Basket/Basket.Function/Startup.cs` (and `Shared/Configuration/ServiceRegistration.cs`)
- `src/Services/Catalog/Catalog.Function/Startup.cs`
- `src/Services/Ordering/Ordering.Function/Startup.cs`
- `src/Services/Review/Review.Function/Startup.cs`
- `src/Services/User/User.Function/Startup.cs`
- `infra/constructs/*-lambdas.ts` — enable `Tracing.ACTIVE`

---

## Consequences

### Positive

- Production incidents become diagnosable: structured, queryable logs with request correlation, and
  end-to-end traces across AppSync → Lambda → DynamoDB/EventBridge instead of a trace that stops at
  the resolver.
- Dev/prod observability asymmetry closes with the right tool per environment (Elasticsearch/Kibana
  locally per ADR-0001/0002; CloudWatch/X-Ray on AWS) instead of forcing one stack into both.
- The documented-but-missing `ServiceDefaults.Lambda` project stops being a doc/reality gap.

### Negative / Costs

- Cold-start overhead: logging formatter is negligible, but SDK instrumentation / an ADOT layer
  adds measurable init time — the implementation must weigh the X-Ray SDK handler (lighter) against
  full OpenTelemetry (richer) with cold start as a first-class criterion.
- CloudWatch Logs and X-Ray have per-GB/per-trace costs (nominal at demo scale).
- One more shared project every Lambda depends on — a bug in it has blast radius across all
  services (which is why [ADR-0024](./0024-testing-strategy-minimum-coverage.md) requires tests
  for BuildingBlocks code).

### Mitigation Strategies

- Start with the minimal footprint (JSON console logging + X-Ray SDK handler on the AWS clients);
  adopt full OTel/ADOT only if trace richness proves insufficient.
- X-Ray sampling rules keep trace cost bounded if traffic ever grows.

---

## References

- [ADR-0001: Elasticsearch Integration Guideline](./0001-elasticsearch-integration-guideline.md)
- [ADR-0002: Elasticsearch Index Lifecycle Management](./0002-elasticsearch-index-lifecycle-management.md)
- [ADR-0007: AppSync (GraphQL) as Client-Facing API](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md)
- [ADR-0024: Testing Strategy and Minimum Coverage Standard](./0024-testing-strategy-minimum-coverage.md)
- `src/BuildingBlocks/BuildingBlocks.ServiceDefaults/Extensions.cs`
- `infra/constructs/appsync-api.ts` (`xrayEnabled: true`)
