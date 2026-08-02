---
tags:
  - status/accepted
  - domain/cross-cutting
---

# ADR-0015: SQS Dead-Letter Queues for CDC Publisher Lambdas and EventBridge Consumer Retry Policy

## Status
**Accepted** — July 2026

---

## Context

[ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md) and
[ADR-0008](./0008-extend-cdc-event-publishing-basket-shoppingcarts-stream-publisher.md) established
the CDC topology (DynamoDB Streams → publisher Lambda → EventBridge → consumer Lambda) used across
all services. That topology is now fully deployed in `infra/constructs/*-lambdas.ts`:

- **Publisher Lambdas** (Streams-triggered, one per table): `basket-shopping-carts-event-publisher`,
  `catalog-stream-event-publisher`, `ordering-order-created-publisher`,
  `review-reviews-event-publisher`. Each already sets `retryAttempts: 3` and
  `bisectBatchOnError: true` on its `DynamoEventSource`, but **none configures an `onFailure`
  destination** — a batch that still fails after 3 retries is simply dropped. There is no queue to
  inspect, and no way to redrive the failed records.
- **Consumer Lambdas** (EventBridge rule targets): `ordering-basket-checkout-consumer`
  (`ordering-basket-checkout-consumer-rule`) and `catalog-review-created-consumer`
  (`review-created-consumer-rule`). Both use a bare `targets.LambdaFunction(fn)` with **no retry
  policy and no dead-letter queue** — EventBridge falls back to its default rule-target retry
  behavior (up to 24 hours / 185 attempts), and an event that never succeeds is silently discarded
  once that window elapses.

This is a reliability gap in the exact place ADR-0005 was written to protect: a permanently
failing handler (bad payload, downstream throttling, a bug) loses the event with no trace, instead
of landing somewhere an operator can see and redrive it.

`infra/` (the CDK app, see [ADR-0014](./0014-deploy-spa-via-opennext-hand-rolled-cdk.md)) is the
only place this applies — locally, Aspire's Lambda/DynamoDB-Streams emulation has no equivalent
failure-destination concept, and EventBridge publishes are already documented as best-effort in
dev (ADR-0004).

---

## Decision

Every CDC publisher Lambda gets an SQS dead-letter queue on its stream event source. Every
EventBridge-triggered consumer gets a **retry policy and dead-letter queue configured on the
EventBridge rule target itself** (not just on the Lambda function).

### 1. Publisher Lambdas (DynamoDB Streams → EventBridge) — SQS DLQ on the event source

Every `DynamoEventSource` MUST set `onFailure` to a per-function SQS queue via
`lambdaEventSources.SqsDlq`. `retryAttempts: 3` (already present on all four) is kept as the
standard for this project — a batch is retried 3 times, `bisectBatchOnError: true` narrows down the
poison record, and if it still fails the batch's failure record goes to the DLQ instead of being
dropped.

```ts
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';

const dlq = new sqs.Queue(this, 'StreamPublisherDlq', {
  queueName: 'basket-shopping-carts-event-publisher-dlq',
  retentionPeriod: cdk.Duration.days(14),
});

this.streamPublisher.addEventSource(
  new lambdaEventSources.DynamoEventSource(shoppingCartsTable, {
    startingPosition: lambda.StartingPosition.TRIM_HORIZON,
    batchSize: 10,
    bisectBatchOnError: true,
    retryAttempts: 3,
    onFailure: new destinations.SqsDlq(dlq), // NEW
  }),
);
```

Applies to all four publishers: `basket-shopping-carts-event-publisher`,
`catalog-stream-event-publisher`, `ordering-order-created-publisher`,
`review-reviews-event-publisher`.

### 2. Consumer Lambdas (EventBridge rule → Lambda) — retry + DLQ on the rule target

The retry count and DLQ MUST be configured as part of the **EventBridge target**, using CDK's
`targets.LambdaFunction` options — this is EventBridge's own retry policy (`retryAttempts`,
`maxEventAge`) and dead-letter config, distinct from the Lambda function's own async-invoke
settings. After 3 failed attempts, EventBridge sends the original event envelope to the queue.

```ts
const dlq = new sqs.Queue(this, 'BasketCheckoutConsumerDlq', {
  queueName: 'ordering-basket-checkout-consumer-dlq',
  retentionPeriod: cdk.Duration.days(14),
});

basketCheckoutRule.addTarget(
  new targets.LambdaFunction(this.basketCheckoutConsumer, {
    retryAttempts: 3,                  // NEW — stop retrying after 3 attempts
    maxEventAge: cdk.Duration.hours(1), // NEW — bound how long EventBridge keeps retrying
    deadLetterQueue: dlq,               // NEW — failed event lands here, not discarded
  }),
);
```

Applies to both current consumers: `ordering-basket-checkout-consumer`
(`ordering-basket-checkout-consumer-rule`) and `catalog-review-created-consumer`
(`review-created-consumer-rule`).

### 3. Naming and ownership — one DLQ per Lambda

DLQs are **not shared** across functions. Each queue is named `<function-name>-dlq` and owned/
provisioned alongside the function it protects, in the same construct file
(`infra/constructs/*-lambdas.ts`). This keeps failure attribution unambiguous when triaging: a
message in `catalog-review-created-consumer-dlq` can only have come from that one consumer.

| Lambda | Role | DLQ |
|---|---|---|
| `basket-shopping-carts-event-publisher` | Publisher | `basket-shopping-carts-event-publisher-dlq` |
| `catalog-stream-event-publisher` | Publisher | `catalog-stream-event-publisher-dlq` |
| `ordering-order-created-publisher` | Publisher | `ordering-order-created-publisher-dlq` |
| `review-reviews-event-publisher` | Publisher | `review-reviews-event-publisher-dlq` |
| `ordering-basket-checkout-consumer` | Consumer | `ordering-basket-checkout-consumer-dlq` |
| `catalog-review-created-consumer` | Consumer | `catalog-review-created-consumer-dlq` |

### 4. Retention

All DLQs use `retentionPeriod: Duration.days(14)` (SQS's maximum) so a failed message survives
long enough for someone to notice and redrive it manually — this project has no automated redrive
pipeline or CloudWatch alarm on `ApproximateNumberOfMessagesVisible`; inspection/redrive is manual
via the console or CLI.

### 5. Constraints going forward

- Every **new** Streams-triggered publisher Lambda MUST configure `onFailure` with a dedicated SQS
  queue when its `DynamoEventSource` is created — this is not optional infrastructure hardening,
  it is part of what makes a publisher Lambda compliant with this ADR.
- Every **new** EventBridge rule targeting a consumer Lambda MUST set `retryAttempts` and
  `deadLetterQueue` on the target, not rely on EventBridge's default retry window.
- `Notification.Go` is out of scope today — it has no CDK-deployed infrastructure yet (it still
  runs only under local Aspire orchestration). When it is deployed to AWS, its EventBridge
  subscription MUST follow the same rule-target retry/DLQ pattern as §2.

---

## Applies To

- `infra/constructs/basket-lambdas.ts` — `streamPublisher` event source
- `infra/constructs/catalog-lambdas.ts` — `streamPublisher` event source, `reviewCreatedConsumer` rule target
- `infra/constructs/ordering-lambdas.ts` — `orderCreatedPublisher` event source, `basketCheckoutConsumer` rule target
- `infra/constructs/review-lambdas.ts` — `reviewCreatedPublisher` event source

---

## Consequences

### Positive

- No integration event is silently dropped after exhausting retries — every failure mode now has a
  queue an operator can inspect and redrive from.
- Consistent, explicit retry budget (3 attempts) across both CDC hops (Streams→publisher and
  EventBridge→consumer), instead of one hop having an explicit policy (publishers) and the other
  falling back to EventBridge's much longer implicit default.
- Failure attribution is immediate: a message's queue name identifies exactly which Lambda failed
  on it, with no shared/ambiguous DLQ to sift through.

### Negative / Costs

- Six new SQS queues to provision and pay for (nominal cost, but non-zero moving parts in a
  learning project).
- No automated redrive or alerting is included — a stuck DLQ message stays invisible until someone
  manually checks it.
- `bisectBatchOnError: true` combined with a Streams DLQ means a poison-pill record can still cause
  several partial-batch failure messages before isolation completes; the DLQ receives the raw
  failure record, not a pre-diagnosed root cause.

### Mitigation Strategies

- The 14-day retention window is deliberately the SQS maximum, giving the widest possible manual
  triage window given no alerting exists yet.
- If this project later adds CloudWatch alarms, the natural metric is
  `ApproximateNumberOfMessagesVisible` on each DLQ — deferred as future work, not required for this
  decision.

---

## References

- [ADR-0004: AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0008: Extend CDC Event Publishing to All Services](./0008-extend-cdc-event-publishing-basket-shoppingcarts-stream-publisher.md)
- [ADR-0011: Review Bounded Context — Product Ratings Aggregated into Catalog via CDC](./0011-review-bounded-context-rating-aggregation-via-cdc.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- `infra/constructs/basket-lambdas.ts`, `catalog-lambdas.ts`, `ordering-lambdas.ts`, `review-lambdas.ts`
