---
name: thin-handlers-rich-domain
description: Use when writing or reviewing a Command/Query handler, an EventsIntegration consumer/publisher, or any domain entity/value object in DuckStore — e.g. "implement this handler", "where should this validation go?", "is this handler doing too much?", "review this handler for business logic". Enforces that handlers only decide "who processes the event" (load, delegate, save) while business rules live in the domain model — the more complex the logic, the less it belongs in the handler and the more it belongs in entities/domain services.
---

# Thin handlers, rich domain

**The rule in one sentence**: a handler's job is to decide *who* processes something — load the
right aggregate/entity, call one domain method, persist the result. It must not decide *how* the
business problem is solved. That decision belongs to the domain model (entities, value objects,
domain services).

This applies to every kind of handler in DuckStore: MediatR `ICommandHandler`/`IQueryHandler`
implementations under `Features/{UseCase}/`, and the CDC/event handlers under
`EventsIntegration/{Consumers,Publishers}` — both shapes are orchestration code, not business logic.

## The complexity gradient

The more complex a piece of logic is, the *less* it belongs in the handler:

| Logic | Where it belongs |
|---|---|
| Load an aggregate by id, call a method, save | Handler — this is orchestration |
| A single conditional guarding an illegal state transition | Domain entity method (e.g. a guard clause at the top of the method) |
| Matching/merging/combining collections of items | Domain entity method |
| Validating a value's shape or business invariant (range, format, allowed combinations) | Value object factory (`Of(...)`, constructor with guards) |
| Multi-step calculation (discounts, tiers, installments, totals) | A domain/application service class, not the handler |
| Aggregating across multiple items before a calculation | A method on the aggregate or a domain service, not a `.Sum()`/`.Select()` inline in the handler |
| Constructing an aggregate from a DTO/event payload | A factory method on the aggregate (e.g. `Order.CreateFromCheckout(dto)`), not a loop in the handler |

If you're about to write an `if`, a `foreach`, a `.Sum()`/`.Select()`, or a multi-line calculation
inside a `Handler.cs`, stop — that's a signal the logic wants to live on the domain type instead.

## What a handler should look like

A handler's body should read like a short story with no branching on business state:

```csharp
public async Task<Result> Handle(MergeBasketCommand command, CancellationToken ct)
{
    var userCart = await _repository.GetBasket(command.UserName, ct);
    var guestCart = await _repository.GetBasket(command.GuestId, ct);

    userCart.Merge(guestCart); // the rule lives on ShoppingCart, not here

    await _repository.StoreBasket(userCart, ct);
    return Result.Success();
}
```

Reference: `src/Services/Basket/Basket.Function/Modules/ShoppingCarts/Features/MergeBasket/Handler.cs`
delegates the item-matching/quantity-combining rule to
`ShoppingCart.Merge(ShoppingCart other)` in
`Modules/ShoppingCarts/Domain/Entities/ShoppingCart.cs`.

Same shape for event consumers:
`src/Services/Ordering/Ordering.Function/Modules/Orders/EventsIntegration/Consumers/PaymentResult/Handler.cs`
loads the `Order`, calls `order.MarkCompleted()` / `order.MarkCancelled()` — each of which guards
its own illegal transition (`if (Status != OrderStatus.Pending) return;` in
`Modules/Orders/Domain/Entities/Order.cs`) — then saves. The handler never inspects `Status` itself.

## Where rich domain logic already lives (study these before adding more)

- `Pricing.Function/Modules/Prices/Features/GetInstallmentPlan/InstallmentCalculator.cs` — a
  static domain service holding the full cost-floor + tier-cap installment/discount math
  (ADR-0028) entirely out of the handler.
- `Pricing.Function/Modules/Campaigns/Domain/ValueObjects/DiscountValue.cs` — `DiscountValue.Of(...)`
  validates the invariant (amount > 0, percentage ≤ 100) at construction; `ApplyTo(nominalPrice)`
  applies the discount. The handler never re-checks these rules.
- `Basket.Function/Modules/ShoppingCarts/Domain/Entities/ShoppingCart.cs` — `Merge`,
  `ApplyDiscounts` own the cart's own rules; the handler just calls them.

## Smells to flag when reviewing a handler

Treat these as findings, not style nits — they mean a future bug fix will have to be applied in
the handler again instead of once in the domain type:

- **Inline aggregation before a calculation** — e.g. computing `totalCost`/`totalOriginalPrice`
  via `.Sum(...)` directly in the handler body before calling a calculator
  (see `Pricing.Function/Modules/Prices/Features/GetBasketInstallmentPlan/Handler.cs` for what
  this looks like — the sum should be a method on the aggregate/service, not handler code).
- **Construction loops in the handler** — building an aggregate's child collection with a
  `foreach` inside the handler/consumer class instead of a single factory method on the aggregate
  (e.g. `Order.CreateFromCheckout(dto)`), as seen in
  `Ordering.Function/Modules/Orders/EventsIntegration/Consumers/BasketCheckout/Handler.cs`.
- **State-transition conditionals in the handler** (`if (order.Status == ...)`) instead of a
  guard clause inside the entity's own method.
- **Validation logic re-implemented in the handler** instead of delegated to a value object's
  factory/constructor.
- **A `FluentValidation` validator that checks business invariants** (not just input shape) —
  simple format/required-field checks are fine in `AbstractValidator<TCommand>`, but invariants
  that depend on domain state belong in the entity, not the validator.

## What NOT to do

- Do not add `if`/`foreach`/aggregation logic to a handler "just this once" because the domain
  method doesn't exist yet — add the method to the entity/value object/domain service first, then
  call it from the handler.
- Do not move business logic into the MediatR pipeline behaviors (`ValidationBehavior`,
  `LoggingBehavior`) — those are cross-cutting concerns, not a place for business rules either.
- Do not reintroduce in-process domain events as a way to "hide" business logic dispatch inside a
  handler (ADR-0005) — CDC via DynamoDB Streams is the only integration-event path.
- Do not treat this as a request to add new abstractions (services, interfaces) beyond what the
  logic actually needs — a guard clause on the entity is enough; don't build a "rules engine" for
  a single `if`.

## When reviewing existing code

1. Open the handler and list every conditional, loop, and calculation in its body.
2. For each one, ask: "does this decide *who* handles the request, or *how* the business problem
   is solved?" If it's the latter, it should move to the entity/value object/domain service the
   handler already depends on (or a new method on it — not a new class unless the logic doesn't
   fit any existing aggregate).
3. Confirm the moved logic keeps its own invariant checks (guard clauses, value object validation)
   so the rule can't be violated by a future caller that skips the handler.
