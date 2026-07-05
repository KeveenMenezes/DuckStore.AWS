# ADR-0024: Testing Strategy and Minimum Coverage Standard

## Status
**Accepted** — July 2026. §1 (`BuildingBlocks.UnitTests`) is implemented; §5 items 2-4
(Review/Catalog/User remediation) remain open backlog.

---

## Context

Test coverage across the solution is wildly asymmetric, and the asymmetry is inverted relative to
risk — the code with the largest blast radius has the least coverage:

| Project | Tests | State |
|---|---|---|
| `Ordering.UnitTests` | 37 | Commands, stream rules, dispatcher, mappers, all 7 ValueObjects |
| `Ordering.FunctionalTests` | 2 | Real Aspire graph + DynamoDB Local, validates GSI/resolver access patterns |
| `Basket.UnitTests` | 17 | Command handlers + `CheckoutedRule` |
| `User.UnitTests` | 3 | `GetProfile` only |
| `Catalog.UnitTests` | 2 | One consumer; stream rule and repositories untested |
| `Review.UnitTests` | 1 | `ReviewStreamImage` only; the publisher Lambda is untested |
| `BuildingBlocks.UnitTests` | 16 *(was 0)* | `EventBridgePublisher`, `StreamRuleDispatcher<T>`, `DynamoIdempotentEventConsumer`, `ValidationBehavior` — the §1 floor |

`BuildingBlocks` is the worst case: `EventBridgePublisher` (whose silent-failure behavior is the
subject of [ADR-0021](./0021-fail-fast-eventbridge-publishing-on-aws.md)),
`StreamRuleDispatcher`, `DynamoIdempotentEventConsumer` and `ValidationBehavior` run inside **every**
service, and none has a single test. A bug there ships to five services at once.

Meanwhile Ordering — the reference architecture per
[ADR-0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) — already
demonstrates the target testing shape; the problem is that no rule requires the other services to
match it.

---

## Decision

A minimum, enforceable testing standard, using the patterns already proven in `Ordering.UnitTests`
and `Ordering.FunctionalTests` (xUnit + Moq + Moq.AutoMock; `DistributedApplicationTestingBuilder`
for functional tests). This ADR sets the floor, not a coverage percentage.

### 1. BuildingBlocks MUST be unit-tested

Shared code has cross-service blast radius, so `tests/BuildingBlocks/BuildingBlocks.UnitTests` MUST
cover, at minimum:

- `EventBridgePublisher` — both modes of ADR-0021: throws on failure when fail-fast, logs-and-
  continues when best-effort; `FailedEntryCount > 0` handling.
- `StreamRuleDispatcher<T>` — rule matching, multiple rules, no-match, rule exception propagation.
- `DynamoIdempotentEventConsumer` — first-time processing, duplicate suppression, the
  `attribute_not_exists(PK)` conditional-write contract.
- `ValidationBehavior` — passes valid commands, throws `ValidationException` with aggregated
  failures.

Any new type added to BuildingBlocks ships with tests in the same PR — no exceptions, because every
service inherits the bug otherwise.

### 2. Per-service unit-test floor

For every `*.Function` service, each of the following MUST have unit tests:

- every **command handler** (`Features/*/Handler.cs`);
- every **stream rule** (`EventsIntegration/Publishers/Rules/*`);
- every **EventBridge consumer** (`EventsIntegration/Consumers/*`), including its idempotency
  short-circuit;
- every **ValueObject** with construction rules (invalid input throws; equality).

Handlers are tested directly with mocked repositories/dependencies (the existing pattern —
e.g. `CheckoutBasketCommandHandlerTests`); no Lambda runtime, no DynamoDB.

### 3. New slices ship with their tests

A PR that adds a feature slice, stream rule, or consumer includes the corresponding unit tests in
the same PR. "Tests later" is not compliant — later never arrived for Catalog, Review, and User,
which is how the current table happened.

### 4. Functional tests — reserved for real-infrastructure contracts

Functional tests (Aspire `DistributedApplicationTestingBuilder` + containers, as in
`Ordering.FunctionalTests`) are slow and Docker-bound, so they are NOT required per feature. They
are required only where correctness depends on real infrastructure behavior that mocks cannot
verify:

- DynamoDB access patterns a mock would trivially fake: GSI key shapes, projections, conditional
  writes — anything an AppSync direct resolver ([ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md))
  relies on but no .NET code exercises.
- Table/stream wiring the seeders create (`DynamoTableInitializer`).

`Ordering.FunctionalTests` (GSI1 query + delete contract) is the model. Basket and Catalog SHOULD
gain equivalents for their resolver-facing access patterns; further breadth is not required.

### 5. Immediate remediation backlog

To bring existing code up to this floor (ordered by blast radius):

1. ~~`BuildingBlocks.UnitTests` — the four components in §1.~~ **Done.**
2. `Review.UnitTests` — the stream publisher (after/with its migration to the rule/dispatcher
   pattern required by ADR-0019).
3. `Catalog.UnitTests` — `CatalogProductChangedRule`, the rating-recompute logic.
4. `User.UnitTests` — remaining handlers as they are added.

---

## Applies To

- `tests/BuildingBlocks/BuildingBlocks.UnitTests`
- `tests/Services/*/*.UnitTests` (all services)
- `tests/Services/Ordering/Ordering.FunctionalTests` (pattern reference); future
  `Basket`/`Catalog` functional test projects

---

## Consequences

### Positive

- The highest-blast-radius code (shared messaging/idempotency/validation) stops being the least
  tested; regressions in BuildingBlocks are caught before they ship to five services.
- A concrete, checkable definition of "tested enough" for PR review — handler/rule/consumer/VO —
  instead of a subjective judgment per PR.
- The Ordering test suites become the explicit template, reinforcing ADR-0019's "one way to build a
  service" with "one way to test a service".

### Negative / Costs

- PRs get bigger and slower to write — the floor is a real gate, and the remediation backlog (§5)
  is non-trivial up-front work.
- Functional tests remain Docker-dependent and slow (minutes of startup), which is why their scope
  is deliberately narrow; the narrow scope means AppSync resolver logic itself (JS runtime) is
  still only exercised in a deployed environment.

### Mitigation Strategies

- The floor targets components, not a coverage percentage — no coverage-tooling gate is introduced,
  keeping enforcement a code-review concern per [ADR-0000](./0000-official-architecture-decisios-records-standard.md).
- Remediation (§5) is ordered by blast radius so partial progress still buys the most protection
  first.

---

## References

- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
- [ADR-0021: Fail-Fast EventBridge Publishing on AWS](./0021-fail-fast-eventbridge-publishing-on-aws.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- `tests/Services/Ordering/Ordering.UnitTests`, `tests/Services/Ordering/Ordering.FunctionalTests`
