# ADR-0008: Extend CDC Integration Event Publishing to All Services — Basket ShoppingCarts Stream Publisher

## Status
**Proposed** — June 2026

---

## Context

[ADR-0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md) established that **all cross-service integration events MUST be published via DynamoDB Streams → publisher Lambda → EventBridge** (CDC). The `Ordering.OrderCreatedPublisher.Lambda` already implements this pattern correctly: an INSERT into `OrderingTable` triggers the Lambda, which publishes `OrderDto` to EventBridge.

The `Basket.Function` service **violates this rule**: `CheckoutBasketCommandHandler` publishes `BasketCheckoutEvent` directly to EventBridge in-process (best-effort), before deleting the basket:

```csharp
// Current — violates CDC rule from ADR-0005
var eventMessage = command.BasketCheckoutDto.Adapt<BasketCheckoutEvent>();
await eventPublisher.PublishAsync(eventMessage, cancellationToken);        // in-process
await basketRepository.DeleteBasket(command.BasketCheckoutDto.UserName, cancellationToken);
```

This creates two structural problems:

- **Reliability gap**: the event can be lost if the process dies between the `PublishAsync` call and the basket deletion — exactly the failure mode CDC eliminates.
- **Inconsistency**: the eventing model differs between services (in-process for Basket, CDC for Ordering), undermining the architecture established by ADR-0004 and ADR-0005.

Additionally, there is no standardised naming convention for publisher Lambdas. `Ordering.OrderCreatedPublisher.Lambda` is event-name-scoped; the rule going forward is table-scoped, matching the one-publisher-per-table CDC topology.

---

## Decision

**Extend CDC event publishing to the Basket service** by creating `Basket.ShoppingCartsEventPublisher.Lambda`, a DynamoDB Streams-triggered Lambda for the `ShoppingCarts` table, and remove the direct EventBridge publish from `CheckoutBasketCommandHandler`.

### 1. Naming convention — one publisher Lambda per DynamoDB table

Publisher Lambdas are named after the service they belong to and the table they consume:

```
<Service>.<TableName>EventPublisher.Lambda
```

| Table | Publisher Lambda | Status |
|---|---|---|
| `ShoppingCarts` | `Basket.ShoppingCartsEventPublisher.Lambda` | New (this ADR) |
| `OrderingTable` | `Ordering.OrderCreatedPublisher.Lambda` | Existing — name is kept as-is for continuity |

Future tables that need to emit integration events follow the same convention.

### 2. Checkout flow — write-before-publish pattern

The `CheckoutBasketCommandHandler` MUST write the checkout payload to DynamoDB before the basket is deleted, so the Stream record is the source of truth. The recommended approach is to update the existing basket item with a `CheckoutData` attribute (serialized `BasketCheckoutDto`) and a `Type = "Checkout"` discriminator **before** deletion:

```mermaid
sequenceDiagram
    participant H as CheckoutBasketCommandHandler
    participant D as DynamoDB (ShoppingCarts)
    participant S as DynamoDB Streams
    participant L as ShoppingCartsEventPublisher.Lambda
    participant E as EventBridge

    H->>D: UpdateItem — add Type="Checkout" + CheckoutData JSON
    Note over D: Committed write = source of truth
    D-->>S: Stream record (MODIFY, NEW_IMAGE)
    S->>L: Invoke (at-least-once)
    L->>E: PublishAsync(BasketCheckoutEvent)
    L->>D: DeleteItem (basket cleanup)
    H->>D: DeleteItem (defensive; idempotent with Lambda's delete)
```

The handler's `DeleteBasket` call at the end is kept as a defensive cleanup; the Lambda also deletes the item, making both sides idempotent.

### 3. `Basket.ShoppingCartsEventPublisher.Lambda` — responsibilities

- Triggered by `MODIFY` Stream records on `ShoppingCarts` where `NewImage["Type"].S == "Checkout"`.
- Maps `NewImage["CheckoutData"].S` (deserialised `BasketCheckoutEvent`) and calls `IEventPublisher.PublishAsync`.
- Deletes the basket item from `ShoppingCarts` after publishing (clean-up; best-effort).
- Does NOT process `INSERT` or `REMOVE` records — those are not checkout events.

### 4. `CheckoutBasketCommandHandler` changes

- MUST write `Type = "Checkout"` and `CheckoutData = <serialised BasketCheckoutDto>` to the existing basket item via `UpdateItem` before deletion.
- MUST remove the `IEventPublisher.PublishAsync` call — the Stream Lambda owns the publish.
- The `IEventPublisher` dependency is removed from `CheckoutBasketCommandHandler`.

### 5. DynamoDB Streams enablement

DynamoDB Streams MUST be enabled with `StreamViewType = NEW_IMAGE` on `ShoppingCarts`. This is added to `Basket.Function/Data/DynamoTableInitializer.cs` (`EnsureBasketTableCreatedAsync`).

### 6. AppHost wiring

`BasketExtensions.cs` registers the new Lambda and attaches the Streams event source:

```csharp
builder.AddAWSLambdaFunction<Projects.Basket_ShoppingCartsEventPublisher_Lambda>(
        "basket-shoppingcarts-event-publisher",
        lambdaHandler: "Basket.ShoppingCartsEventPublisher.Lambda::...Function::FunctionHandler")
    .WaitFor(dynamoDb)
    .WithReference(dynamoDb)
    .WithDynamoDBStreamsEventSource("ShoppingCarts")
    .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
    .WithAwsDevEnvironment();
```

The existing `basket-checkout-basket` Lambda registration removes its `EventBridge__BusName` environment variable (no longer publishes events).

---

## Applies To

- `src/Services/Basket/Basket.Function` — `CheckoutBasketCommandHandler`, `DynamoTableInitializer`
- `src/Services/Basket/Basket.ShoppingCartsEventPublisher.Lambda` — new project
- `src/AppHost/BasketExtensions.cs` — new Lambda registration + Streams source
- `src/AppHost/AppHost.csproj` — new project reference

---

## Consequences

### Positive

- Basket integration events are now **at-least-once reliable** via DynamoDB Streams, eliminating the in-process best-effort gap.
- Consistent CDC model across all services (Basket and Ordering follow identical topology).
- Enforces ADR-0005's constraint: no in-process direct EventBridge publishes for cross-service events.

### Negative / Costs

- Checkout latency increases slightly — the Stream event is asynchronous, so downstream consumers (`Ordering.BasketCheckoutConsumer.Lambda`, `Notification.Go`) receive the event after the Lambda invocation, not inline.
- Basket service now requires two write operations on checkout (update + delete) instead of one delete.
- DynamoDB Streams must be enabled on `ShoppingCarts`, adding a small cost and operational dependency.

### Mitigation Strategies

- The latency increase is acceptable: downstream consumers already tolerate EventBridge propagation delay. No SLA commitments exist on checkout-to-order creation time in this learning project.
- The `MODIFY` discriminator (`Type = "Checkout"`) keeps the Lambda invocation narrow — only checkout events trigger publishing logic; other basket mutations are ignored.

---

## References

- [ADR-0004: AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- `Ordering.OrderCreatedPublisher.Lambda` — reference implementation for the publisher Lambda pattern
- `Basket.Function/Features/CheckoutBasket/CheckoutBasketHandler.cs` — handler to be modified
- `src/AppHost/BasketExtensions.cs` — AppHost wiring to be updated
