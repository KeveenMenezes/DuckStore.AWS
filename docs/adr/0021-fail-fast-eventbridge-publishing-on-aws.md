---
tags:
  - status/accepted
  - domain/cross-cutting
---

# ADR-0021: Fail-Fast EventBridge Publishing on AWS — Best-Effort Only in Local Dev

## Status
**Accepted** — July 2026

---

## Context

[ADR-0004](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md) made EventBridge publishes
**best-effort in local dev**, because the bus only exists on AWS. That behavior is implemented in
`src/BuildingBlocks/BuildingBlocks.Messaging/EventBridge/EventBridgePublisher.cs`, which is correct
locally but runs **identically inside the Streams-triggered publisher Lambdas on AWS**:

- `PutEventsAsync` is wrapped in a `catch (Exception)` that only calls `LogWarning` and returns —
  the method never throws, in any environment.
- A response with `FailedEntryCount > 0` (throttling, malformed detail, entry-level rejection) is
  also only logged as a warning.

The consequence on AWS breaks the durability story that
[ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md) exists to provide:

1. A publisher Lambda calls `PublishAsync`, EventBridge genuinely fails (IAM, throttling, outage,
   bad payload).
2. The publisher swallows the failure and the Lambda invocation **reports success**.
3. The DynamoDB Streams event source advances its shard checkpoint past the record.
4. The integration event is **lost permanently** — no retry, and the retry/bisect/DLQ machinery
   specified by [ADR-0015](./0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md) can
   never fire, because from the event source's point of view nothing ever failed.

In other words, ADR-0015's DLQs protect against a *handler* that throws — but the current publisher
guarantees the handler never throws. The two decisions only deliver durability together: the
publisher must fail fast on AWS (this ADR) so that the Streams retry → bisect → DLQ pipeline
(ADR-0015) has something to act on.

---

## Decision

`EventBridgePublisher` becomes **environment-aware**: on AWS every publish failure throws; outside
AWS the current best-effort/log-and-continue behavior is kept.

### 1. Failure modes that MUST throw on AWS

When running on AWS, `PublishRawAsync` MUST surface both failure modes as exceptions:

- Any exception from `PutEventsAsync` (transport, IAM, availability) — logged as **Error** and
  rethrown, not caught-and-warned.
- A successful HTTP response with `FailedEntryCount > 0` — logged as **Error** including the entry's
  `ErrorCode`/`ErrorMessage`, then an `EventPublishException` (new, in
  `BuildingBlocks.Messaging.EventBridge`) is thrown. A non-throwing partial failure is
  indistinguishable from success to the caller, which is exactly the bug being fixed.

The exception propagates out of the `[LambdaFunction]` stream handler, the invocation fails, and
the `DynamoEventSource` policy takes over: retry up to 3 times, `bisectBatchOnError` isolates the
poison record, and the failure record lands in the publisher's SQS DLQ (ADR-0015).

### 2. Environment detection

The switch is the standard Lambda runtime variable, with an explicit configuration override:

```csharp
// Fail fast when running inside a real Lambda runtime; best-effort everywhere else.
// EventBridge__FailFast (bool) overrides the detection in either direction when set.
private bool FailFast =>
    options.Value.FailFast
    ?? Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") is not null;
```

- `AWS_LAMBDA_FUNCTION_NAME` is set by the AWS Lambda runtime. It is **not** set by the Aspire
  Lambda service emulator's host process environment injected via AppHost, so local runs keep
  best-effort semantics with no configuration changes. If the emulator is ever found to set it,
  the AppHost `WithAwsDevEnvironment()` helper MUST pin `EventBridge__FailFast=false` for local
  functions — the override exists precisely so detection never has to be perfect.
- `EventBridgeOptions` gains `public bool? FailFast { get; set; }` (default `null` = auto-detect),
  bound from the existing `EventBridge` configuration section.

### 3. What stays best-effort

- All local/Aspire runs (the bus does not exist locally; ADR-0004 semantics are unchanged).
- Nothing else. There is deliberately **no** per-call opt-out: a publisher Lambda on AWS may not
  choose to swallow a failure, because the caller cannot know better than the retry/DLQ pipeline
  what to do with it.

### 4. Correct / Incorrect

```csharp
// ✅ Correct — on AWS, a failed entry throws so the stream checkpoint does not advance
if (response.FailedEntryCount > 0)
{
    var failure = response.Entries.Find(e => e.ErrorCode != null);
    logger.LogError("Failed to publish {DetailType}: {ErrorCode} - {ErrorMessage}",
        detailType, failure?.ErrorCode, failure?.ErrorMessage);

    if (FailFast)
        throw new EventPublishException(detailType, failure?.ErrorCode, failure?.ErrorMessage);
}

// ❌ Incorrect — the current code: warn-and-return in every environment,
// which reports success to the Lambda runtime and loses the event on AWS
catch (Exception ex)
{
    logger.LogWarning(ex, "EventBridge unavailable while publishing {DetailType}; skipping.", detailType);
}
```

---

## Applies To

- `src/BuildingBlocks/BuildingBlocks.Messaging/EventBridge/EventBridgePublisher.cs`
- `src/BuildingBlocks/BuildingBlocks.Messaging/EventBridge/EventBridgeOptions.cs`
- All CDC publisher Lambdas that depend on `IEventPublisher`: Basket, Catalog, Ordering, Review
  stream publishers (no code change needed in the services — behavior changes via the shared
  publisher).

---

## Consequences

### Positive

- Restores the end-to-end durability promise of ADR-0005: a failed publish on AWS is retried by the
  Streams event source and, if permanently failing, lands in an inspectable DLQ instead of
  vanishing.
- Makes ADR-0015's infrastructure actually reachable — without this change its DLQs can never
  receive a publish failure.
- Local development experience is unchanged: no bus, no errors, no extra configuration.

### Negative / Costs

- A single poison event entry can fail a whole stream batch until `bisectBatchOnError` isolates it,
  temporarily delaying other events in the same shard (ordered processing is the point of Streams,
  so this is inherent, but it becomes visible where it was previously silent).
- Environment auto-detection is heuristic; a misconfigured local environment that happens to set
  `AWS_LAMBDA_FUNCTION_NAME` would turn local publishes into hard failures.

### Mitigation Strategies

- The `EventBridge__FailFast` configuration override makes detection failures a one-line fix in
  either direction (AppHost env var locally, Lambda env var on AWS).
- Unit tests for both modes of `EventBridgePublisher` (throwing on AWS, swallowing locally) are
  mandated by [ADR-0024](./0024-testing-strategy-minimum-coverage.md).

---

## References

- [ADR-0004: AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0008: Extend CDC Event Publishing to All Services](./0008-extend-cdc-event-publishing-basket-shoppingcarts-stream-publisher.md)
- [ADR-0015: SQS Dead-Letter Queues for CDC Publisher Lambdas and EventBridge Consumer Retry Policy](./0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md)
- [ADR-0024: Testing Strategy and Minimum Coverage Standard](./0024-testing-strategy-minimum-coverage.md)
- `src/BuildingBlocks/BuildingBlocks.Messaging/EventBridge/EventBridgePublisher.cs`
