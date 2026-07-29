# ADR-0004: AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge

## Status
**Accepted** — June 2026

---

## Context

DuckStore currently uses **MassTransit** over **RabbitMQ** (`BuildingBlocks.Messaging`, `AddMessageBroker()`) as the asynchronous integration mechanism between services (e.g. Basket → Ordering, Ordering domain events → integration events).

The project's direction is changing:

- All services are being migrated towards a **serverless architecture** running on **AWS Lambda**, replacing the current long-running ASP.NET Core hosts.
- The project officially adopts an **AWS-First** strategy: prefer managed AWS services over self-hosted infrastructure (containers, brokers) whenever a suitable AWS-native equivalent exists.
- RabbitMQ requires a dedicated broker process, which is at odds with a serverless/Lambda execution model (no long-lived consumers, no persistent broker to manage, pay-per-invocation).
- The `Notification` service already consumes from **SQS** (via LocalStack in dev), so there is precedent and partial infrastructure for AWS-native messaging in the solution.
- The team evaluated **SQS+SNS** vs **EventBridge** as the replacement (see prior discussion): EventBridge was preferred for its content-based routing/filtering on event payloads, native Lambda targets, and closer alignment with the `IntegrationEvent` semantics already used in `BuildingBlocks.Messaging`, despite higher per-event cost/latency than SNS — acceptable trade-offs for a study project optimizing for learning value and serverless-native patterns.

---

## Decision

Adopt **Amazon EventBridge** as the central event bus for asynchronous service-to-service communication, replacing **MassTransit** and **RabbitMQ**.

- **RabbitMQ** is removed from local infrastructure (`AppHost`, `docker-compose`) and from all environments.
- **MassTransit** is removed as a dependency; `BuildingBlocks.Messaging` is reworked so that publishing an `IntegrationEvent` puts an event onto an **EventBridge custom event bus**, instead of publishing to a RabbitMQ exchange.
- **EventBridge rules target Lambda functions directly** by default (rule → Lambda target), relying on EventBridge's own retry policy and dead-letter queue configuration per rule. **SQS is not a mandatory intermediary for every consumer.**
- **SQS is introduced selectively**, only when a specific consumer needs it — e.g. to control concurrency/throughput against a downstream dependency, to batch multiple events per Lambda invocation, or to apply consumer-specific retry/visibility-timeout semantics beyond what an EventBridge rule's DLQ offers. In those cases the flow becomes EventBridge rule → SQS queue → Lambda (event source mapping), as already done by the `Notification` service.
- Event routing is defined via **EventBridge rules** based on event type/source (content-based routing), replacing topic/exchange bindings previously configured by MassTransit conventions.
- In local development, EventBridge and SQS are emulated via **LocalStack**, consistent with the existing setup for the `Notification` service.
- This decision applies project-wide as services are migrated to Lambda; services not yet migrated may continue publishing/consuming through the new EventBridge-based abstraction in `BuildingBlocks.Messaging` without requiring a Lambda runtime themselves.

---

## Consequences

### Positive
- Removes the operational burden of running and maintaining a RabbitMQ broker (containers, queues, exchanges, HA concerns).
- Aligns messaging infrastructure with the serverless/Lambda direction of the project (no persistent consumer process required).
- Native integration between EventBridge and Lambda (rule → target) reduces glue code compared to MassTransit consumer hosting — most consumers need no queue at all.
- Content-based routing rules map naturally onto the existing `IntegrationEvent` model, without needing per-event-type topics.
- SQS stays available as an opt-in tool for the few consumers that genuinely need batching or throughput control, instead of being forced onto every Lambda — reduces unnecessary moving parts.
- Higher educational value: EventBridge + SQS + Lambda is a common production pattern for serverless event-driven systems.

### Negative
- Removing MassTransit means losing its built-in outbox/inbox, retry policies, and saga support; equivalent behavior (e.g. transactional outbox in `Ordering.Infrastructure`) must be re-implemented or adapted to publish to EventBridge directly.
- EventBridge has higher per-event latency and cost than direct RabbitMQ messaging or SNS — acceptable for a study project, but worth noting if this pattern is reused outside this context.
- Local development now depends on LocalStack's EventBridge/SQS emulation fidelity, instead of a real RabbitMQ broker.
- Event routing rules move out of application code and into AWS configuration/IaC, requiring discipline (e.g. CDK/Terraform) to avoid undocumented "magic" routing.
- All existing consumers (e.g. `Ordering` MassTransit inbox/outbox, RabbitMQ-based consumers) must be migrated, which is a breaking change to `BuildingBlocks.Messaging`'s public surface.

---

## References
- [[ADR-0000]] Official Architecture Decision Records Standard
- AWS-First strategy discussion (project chat, June 2026)
- Existing `Notification` service SQS/DynamoDB via LocalStack precedent
