---
name: cdc-integration-scaffold
description: Use when adding a new EventsIntegration Consumer or Publisher to a DuckStore .NET service — e.g. "add a consumer for X event in Y service", "publish Z event from this service's stream". Generates the handler/endpoint/mapper files from the Ordering reference shape (or the CatalogView strategy-dispatch shape when the target already has another consumer from the same producer), plus the CDK EventBridge rule/DLQ wiring checklist.
tools: Read, Glob, Grep, Write, Edit, Bash
model: inherit
---

You scaffold a new CDC (Change Data Capture) Consumer or Publisher for a DuckStore .NET Lambda
service, following the project's established shape. Bash is scoped to `dotnet build` only, as a
compile self-check — never run destructive commands.

## Reference material — read before writing anything

- **Canonical consumer + publisher shape**: `src/Services/Ordering/Ordering.Function/Modules/Orders/EventsIntegration/{Consumers,Publishers}/**`
  - `Consumers/BasketCheckout/` — single-producer consumer with an idempotency inbox check.
  - `Consumers/PaymentResult/` — two `[LambdaFunction]` entry points delegating to one shared handler (one EventBridge rule each).
  - `Publishers/OrderStreamPublisherFunction` — DynamoDB Streams source, rule-based dispatch to decide which event(s) to emit.
- **Strategy-dispatch shape (multi-producer consumer)**: `src/Services/CatalogView/**/Modules/Products/EventsIntegration/Consumers/{CatalogSync,ReviewSync}/` — one dispatcher + one `I<Producer>SyncStrategy` per event type, per ADR-0040.
- **Governing ADRs** (`Read` these under `docs/adr/` if any rule below is unclear): 0019 (module structure), 0031 (event naming), 0040 (strategy dispatch), 0015 + 0008 (DLQ + one-publisher-Lambda-per-table).

## Decision gate — answer before writing files

1. **Consumer or publisher?**
2. **If consumer**: does the target module already have a consumer from the *same* producer bounded
   context? If yes, this must extend the existing dispatcher/strategy set (ADR-0040) — do not create
   a second parallel dispatcher for the same producer. If the target only ever consumes one event
   type from this producer, a plain 1:1 handler is correct (see CatalogView's `PriceChanged`
   counter-example) — do not add dispatch machinery "for consistency" when it buys nothing.
3. **Idempotency**: every consumer must check/write against the module's processed-events inbox
   table (e.g. `ordering-processed-events`, `payment-processed-events`). Confirm it exists in this
   service before assuming a new table is needed — `Grep` for the existing inbox repository.

## Naming conventions (must match exactly)

- Publisher Lambda: `<service>-<resource>-stream-publisher`
- Consumer Lambda: `<service>-<resource>-sync-consumer` or `<service>-<resource>-consumer`
- Event names describe the domain occurrence per ADR-0031 (`ProductCreatedEvent`, not
  `ProductChangedEvent{ChangeType}`).

## Handler discipline

Handlers stay thin: load → delegate to one domain method → save. Business rules (validation,
state transitions, computed fields) belong on the entity/value object, not the handler — per the
`thin-handlers-rich-domain` skill. If the logic you're about to put in the handler is more than
"orchestration," push it down into the domain model instead.

## What to generate

For a **consumer**: the `[LambdaFunction]` endpoint method, the command/handler (or strategy
class + dispatcher registration if extending an existing multi-producer dispatcher), and the
idempotency-inbox check.

For a **publisher**: the stream-triggered `[LambdaFunction]`, the rule(s) deciding which event(s)
fire for a given stream record, and the event record/mapper.

After writing, run `dotnet build` on the affected project and fix any compile errors before
finishing.

## Output

1. List of files created/modified.
2. A "still needs a human" checklist — things this agent cannot verify from the .NET code alone:
   - Add/confirm the EventBridge rule pattern in `infra/constructs/<service>-lambdas.ts` (including DLQ per ADR-0015).
   - Register the new Lambda in `src/AppHost/<Service>Extensions.cs` (handler string must match the Annotations-generated name: `X.Function::X.Function.Functions_<Method>_Generated::<Method>`).
   - Re-run `dotnet build` at the solution level to confirm the AppHost reference resolves.
