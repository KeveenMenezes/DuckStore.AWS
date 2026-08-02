---
tags:
  - status/accepted
  - domain/cross-cutting
---

# ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers

## Status
**Accepted** — July 2026

---

## Context

The Lambda services drifted into three different internal layouts, which makes moving between them and onboarding harder than it should be for a demo/reference codebase:

- **Catalog** is already module-based: `Modules/Products/*`, `Modules/Categories/*` (each with `Data`, `Features`, `EventsIntegration`, domain types) plus a `Shared/` folder.
- **Basket** and **Ordering** are flat — no `Modules/` — with domain types in top-level `Models/`, `ValueObjects/`, `Dtos/`, `Enums/`, `Exceptions/`.
- Feature slices are named three different ways: combined single file (`CreateProductHandler.cs` in Catalog), prefix-per-file (`StoreBasketEndpoint.cs`/`StoreBasketHandler.cs` in Basket), and split-per-type (`GetOrdersByCustomerQuery.cs`/`Endpoint.cs`/`Handler.cs` in Ordering).

Cross-service event **publishing** has its own problem. Every DynamoDB Streams publisher is a bespoke, single handler that inspects `record.EventName` / `NewImage` inline and calls the publisher directly (Ordering `OrderCreatedPublisherFunction`, Basket `ShoppingCartsEventPublisherFunction`, Catalog `CatalogStreamEventPublisherFunction`, Review `ReviewCreatedPublisherFunction`). Two consequences:

- **No before/after view.** The streams are provisioned `NEW_IMAGE` only, and the handlers never compare old vs new — so a publisher literally cannot express "publish when Status changed from X to Y". Order fulfillment (already gated behind the `OrderFullfilment` flag) needs exactly that: distinct events per status transition (payment approved, payment refused, order shipped …).
- **Business rules tangled with AWS.** Deciding *what* to publish and *how* to reach EventBridge live in the same method, so the decision logic is hard to unit-test without the transport.

We want one internal structure for every service, and a stream-publisher pattern that separates "should this be published?" from "how does it reach EventBridge".

---

## Decision

### 1. `Modules/` per aggregate, plus a service-level `Shared/`

Every service is organized as **one module per aggregate** under `Modules/`, with cross-module concerns under `Shared/`:

```
<Service>.Function/
├── Shared/            # Configuration (LambdaStartup/ServiceRegistration, GlobalUsings),
│                      #   Data (idempotency inbox), Exceptions, abstractions, common infra
├── Modules/
│   └── <Aggregate>/   # Data, Features, EventsIntegration, Domain
└── Startup.cs         # [LambdaStartup] -> Shared/Configuration/ServiceRegistration
```

Module names are **plural** (the aggregate's collection), matching the existing Catalog modules: `Modules/Products` (entity `Product`), `Modules/Orders` (entity `Order`). This is not cosmetic — a singular module folder `Order` produces a namespace segment `…Modules.Order`, which **shadows the `Order` entity type** (`CS0118: 'Order' is a namespace but is used like a type`). Plural module folders avoid the collision.

### 2. `Domain/` groups the pure domain types

Inside a module, the domain model lives under a `Domain/` folder, separated from the data/application/messaging code:

```
Modules/Orders/
├── Data/                       # repositories, DynamoDB config, mappings, queries
├── Features/                   # AppSync/HTTP Queries & Mutations (one folder per action)
├── EventsIntegration/          # Consumers/, Publishers/, Events/
└── Domain/
    ├── Entities/               # aggregate roots + entities  (was Models/)
    ├── ValueObjects/
    ├── Enums/
    └── Dtos/
```

### 3. Feature/Consumer slices use generic file names

A slice is a folder named after the action; the files inside are **generic**, because the folder already supplies the context:

```
Features/CreateProduct/
├── Endpoint.cs    # entry point (HTTP Lambda method / consumer Lambda handler)
├── Handler.cs     # business rule (MediatR command/query handler)
├── Validator.cs   # optional — omit when there is nothing to validate
└── Mapper.cs      # optional — inline trivial maps in the Handler/Endpoint instead
```

Consumers under `EventsIntegration/Consumers/<Event>/` follow the **same shape** (the Lambda entry class is `Endpoint.cs`). Validator/Mapper are created only when they carry real logic; feature-specific exceptions may stay inline in the Handler when not reused.

> **Namespace exception for HTTP endpoints.** The `Amazon.Lambda.Annotations` generator emits handler types from the namespace of the `partial class Functions`. A partial class must live in a single namespace, and the generated handler string is referenced by the AppHost. So the `Functions` partial declarations (the `Endpoint.cs` HTTP files) stay in the **root** namespace (`Ordering.Function`) even though the file sits in `Modules/Orders/Features/<Action>/`. Everything else uses folder-matching namespaces.

### 4. Stream publishers use a Rule/Strategy pattern — but only when they need it

A DynamoDB Streams publisher that emits **different events per state transition (2+ conditions)** MUST use the rule-based pattern below. A publisher with a **single, unconditional trigger** (e.g. Catalog publishing on every change, Review on every insert) MAY stay a plain handler — do not add ceremony where there is one rule.

The pattern lives in `BuildingBlocks.Messaging`:

```csharp
// A rule answers "should this be published?" and, if so, returns a transport-agnostic instruction.
public sealed record PublishInstruction(string DetailType, object Payload);

public sealed record StreamContext<TImage>(string EventName, TImage? Old, TImage? New);

public interface IStreamRule<TImage>
{
    bool Match(StreamContext<TImage> context);
    Task<PublishInstruction> BuildAsync(StreamContext<TImage> context, CancellationToken ct = default);
}
```

The Lambda handler only builds a `StreamContext` (old + new image, projected into a module-owned snapshot) and hands it to the `StreamRuleDispatcher<TImage>`. The dispatcher runs each rule and, for those that match, publishes the returned instruction. The **only** component that knows EventBridge is `IEventPublisher`:

```csharp
Task PublishAsync(PublishInstruction instruction, CancellationToken ct = default);
// EventBridgePublisher: PublishRawAsync(instruction.DetailType, JsonSerializer.Serialize(instruction.Payload), ct)
```

**Incorrect** — the rule reaches into AWS and decides transport:

```csharp
public async Task ExecuteAsync(StreamContext<OrderStreamImage> ctx) =>
    await _eventBridge.PutEventsAsync(new PutEventsRequest { /* ... */ }); // rule bound to AWS
```

**Correct** — the rule is pure domain logic and returns an instruction:

```csharp
public sealed class OrderCreatedRule(IOrderRepository orders) : IStreamRule<OrderStreamImage>
{
    public bool Match(StreamContext<OrderStreamImage> ctx) =>
        ctx.EventName == "INSERT" && ctx.New?.Type == "Order";

    public async Task<PublishInstruction> BuildAsync(StreamContext<OrderStreamImage> ctx, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(ctx.New!.Id, ct);
        return new PublishInstruction(nameof(OrderCreatedEvent), order!.ToOrderCreatedEvent());
    }
}
```

Because rules detect *transitions*, the source table's stream must be provisioned `NEW_AND_OLD_IMAGES` (the Ordering table now is). A rule may depend on a domain abstraction such as `IOrderRepository`; it must never depend on `IAmazonEventBridge` or build a `PutEventsRequest`.

---

## Applies To

- `src/Services/Ordering/Ordering.Function` — the **reference implementation** of this ADR (`Modules/Orders`, `Shared/`, `OrderCreatedRule` + `StreamRuleDispatcher`).
- `src/Services/Catalog/Catalog.Function` — already module-based; normalize `Models/` → `Domain/Entities/` and file naming when next touched.
- `src/Services/Basket/Basket.Function`, `src/Services/Review/Review.Function` — migrate to the layout incrementally.
- `src/BuildingBlocks/BuildingBlocks.Messaging` — home of `PublishInstruction`, `StreamContext<TImage>`, `IStreamRule<TImage>`, `StreamRuleDispatcher<TImage>`, and the `IEventPublisher` overload.

---

## Consequences

### Positive

- **One structure to learn.** Every service reads the same way; a slice is always `Endpoint`/`Handler`/(`Validator`)/(`Mapper`) in an action folder.
- **Publisher rules are unit-testable in isolation.** `Match` is a pure predicate; `BuildAsync` returns a value; neither needs EventBridge. The dispatcher is covered once in shared tests.
- **Transitions become expressible.** `StreamContext` carries old + new, so fulfillment status rules (`PaymentApprovedRule`, `OrderShippedRule`, …) can be added without reworking the publisher.
- **Clean seam.** Rules ↔ business, `IEventPublisher` ↔ EventBridge; adding a new event is adding a rule, not editing a handler.

### Negative / Costs

- **Migration churn.** Moving a flat service into `Modules/<Aggregate>/…` touches every file's namespace and ripples into that service's seeder and unit tests (all funneled through their `GlobalUsings`).
- **One extra folder level** and a `Domain/` indirection for single-aggregate services.
- **Lambda handler strings must be updated.** Relocating a plain-class stream/consumer Lambda changes its fully-qualified name; the matching `lambdaHandler` string in the AppHost extension must change with it (handler strings are not compile-checked).

### Mitigation Strategies

- Migrate **one service at a time** (Ordering first) and keep the HTTP `Functions` partial in the root namespace so generated handler strings — and their AppHost registrations — don't move.
- After relocating any stream/consumer Lambda, grep the AppHost `*Extensions.cs` for its old namespace and update the `lambdaHandler` string; boot the app and confirm the event still flows.

### Future Constraints

- New modules MUST follow `Modules/<PluralAggregate>/{Data,Features,EventsIntegration,Domain}` with `Shared/` for cross-module concerns.
- A stream publisher that emits more than one event type, or reacts to a state transition, MUST route through `StreamRuleDispatcher`; a rule MUST return a `PublishInstruction` and MUST NOT reference EventBridge types.

---

## Related Documentation

- [ADR-0004: AWS-First — EventBridge over MassTransit/RabbitMQ](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0005: Remove Domain Events; CDC via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0012: Merge Discount into Basket; Coupon as In-Process Entity](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md)
- [ADR-0031: CDC Events Named After the Domain Occurrence, Never a Raw ChangeType Discriminator](./0031-cdc-events-named-after-domain-occurrence-no-changetype-discriminator.md) — narrows §4's "single unconditional trigger" allowance
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)

## References

- `.claude/skills/module-structure/SKILL.md` — how to apply this ADR when adding a service/module/slice.
- Amazon DynamoDB Streams — `StreamViewType.NEW_AND_OLD_IMAGES`.
- Amazon.Lambda.Annotations — generated handler naming.
