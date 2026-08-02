---
tags:
  - status/accepted
  - domain/cross-cutting
---

# ADR-0031: CDC Integration Events Named After the Domain Occurrence, Never a Raw ChangeType Discriminator

## Status
**Accepted** — July 2026

---

## Context

Two integration events published by Catalog's CDC stream publisher leaked DynamoDB Streams'
persistence vocabulary straight into their contract:

- `CatalogUpdatedEvent` (`ChangeType`, `ProductId`) — built by `CatalogProductChangedRule`, whose
  `Match` fired on **any** change (`INSERT`/`MODIFY`/`REMOVE` alike), copying `context.EventName`
  verbatim into `ChangeType`.
- `CatalogProductSyncEvent` (`ChangeType` + the full product payload) — built by
  `CatalogSearchSyncRule`, the same "any change" `Match`.

Consumers then branched on that raw string instead of reacting to a named domain event:

```csharp
// Pricing — Modules/Prices/EventsIntegration/Consumers/CatalogProductRemoved/Endpoint.cs
if (evt.Detail.ChangeType != "REMOVE")
    return;
```

```csharp
// CatalogView — Modules/Products/EventsIntegration/Consumers/ProductSync/Handler.cs
evt.ChangeType switch
{
    "REMOVE" => index.DeleteAsync(evt.ProductId, cancellationToken),
    _ => index.UpsertAsync(ToDocument(evt), cancellationToken)
};
```

This contradicts the already-correct examples in the same codebase, which each match one specific
domain occurrence and emit one named event, with no discriminator field:

- `OrderCreatedRule` (Ordering) — `Match: ctx.EventName == "INSERT"` → `OrderCreatedEvent`.
- `ReviewCreatedRule` / `ReviewUpdatedRule` (Review) — one rule per occurrence, each with its own
  event. The SPA revalidator already subscribes to **both** detail-types on the same
  `sst.aws.Bus.subscribe` (`detailType: ["ReviewCreatedEvent", "ReviewUpdatedEvent"]`) — proof that
  "one Lambda, several named detail-types" already works end-to-end in this repo, with no
  discriminator field needed.
- `CatalogCategorySyncRule` (Catalog) — matches only `EventName == "MODIFY"` with an actual name
  change; no `ChangeType` field.

The problem is not the rule-based publisher pattern itself (ADR-0019) — it's that a rule matching
**any** change and forwarding the raw Streams event name is really **three distinct domain
occurrences (created/updated/deleted) disguised as one rule via a discriminator field**, pushed
onto every consumer to unpack. A consumer that only cares about deletes (Pricing) still receives
and discards every create/update; a consumer that treats create/update identically (CatalogView's
upsert) still has to special-case delete inside the same handler. ADR-0019 §4 explicitly allowed
this shape ("a single unconditional trigger — e.g. Catalog publishing on every change — MAY stay a
plain handler"), and that allowance is what let it happen.

---

## Decision

1. **A CDC integration event's name and shape MUST describe what happened to the aggregate**
   (`ProductCreatedEvent`, `OrderCreatedEvent`, `ReviewUpdatedEvent`) — never the persistence
   mechanism that produced it.
2. **An integration event record MUST NOT carry a field that mirrors DynamoDB Streams' `EventName`**
   (`ChangeType`, `EventName`, or equivalent) for a consumer to branch on.
3. **A stream-rule publisher that would need such a discriminator — because it reacts to 2+
   distinct occurrences with different consumer-relevant meaning — MUST split into one
   `IStreamRule<TImage>` per occurrence**, each with a narrow `Match` (e.g. `EventName == "INSERT"`)
   and its own named `DetailType`/event.
4. **Consumers MUST subscribe only to the detail-types they need** — one EventBridge rule per
   detail-type per Lambda — instead of one broad rule with an in-handler
   `if (evt.Detail.ChangeType != "X") return;` guard. Precedent: the `ReviewCreatedEvent`/
   `ReviewUpdatedEvent` pair already does this for the SPA revalidator's single `Bus.subscribe`.
5. **A thin, id-only event MAY be shared by multiple consumers** when the occurrence itself (not
   the payload) is all any of them need — e.g. `ProductDeletedEvent` is consumed by both Pricing
   (row cleanup) and CatalogView (search-index delete) via two independent EventBridge rules
   routing to two different Lambdas.

**Incorrect** — one rule fans three occurrences through a discriminator field:

```csharp
public sealed class CatalogProductChangedRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        !string.IsNullOrEmpty(context.New?.Id ?? context.Old?.Id);   // matches everything

    public Task<PublishInstruction> BuildAsync(StreamContext<CatalogStreamImage> context, CancellationToken ct = default) =>
        Task.FromResult(new PublishInstruction(
            nameof(CatalogUpdatedEvent),
            new CatalogUpdatedEvent { ChangeType = context.EventName, ProductId = /* ... */ }));  // leaks INSERT/MODIFY/REMOVE
}
```

**Correct** — one rule per occurrence, each with its own named event:

```csharp
public sealed class ProductCreatedRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        context.EventName == "INSERT" && !string.IsNullOrEmpty(context.New?.Id);

    public Task<PublishInstruction> BuildAsync(StreamContext<CatalogStreamImage> context, CancellationToken ct = default) =>
        Task.FromResult(new PublishInstruction(nameof(ProductCreatedEvent), new ProductCreatedEvent { ProductId = context.New!.Id }));
}

public sealed class ProductDeletedRule : IStreamRule<CatalogStreamImage>
{
    public bool Match(StreamContext<CatalogStreamImage> context) =>
        context.EventName == "REMOVE" && !string.IsNullOrEmpty(context.Old?.Id);

    public Task<PublishInstruction> BuildAsync(StreamContext<CatalogStreamImage> context, CancellationToken ct = default) =>
        Task.FromResult(new PublishInstruction(nameof(ProductDeletedEvent), new ProductDeletedEvent { ProductId = context.Old!.Id }));
}
```

### Amends ADR-0019 §4

ADR-0019 §4 states: *"A publisher with a single, unconditional trigger (e.g. Catalog publishing on
every change, Review on every insert) MAY stay a plain handler."* That allowance is **narrowed**:
it covers a publisher that emits **one** event for **one** kind of occurrence (Review's INSERT-only
rule genuinely has a single trigger). It does **NOT** cover a rule that matches on any `EventName`
and forwards a `ChangeType`/discriminator field — that is 2+ occurrences disguised as one rule, and
MUST use the rule-per-occurrence pattern in this ADR instead. Everything else in ADR-0019 (module
layout, `IStreamRule<TImage>`/`StreamRuleDispatcher`/`PublishInstruction`/`IEventPublisher`
machinery, feature-slice naming) remains valid and unchanged.

---

## Applies To

- `src/Services/Catalog/Catalog.Function` — `Modules/Products/EventsIntegration/Publishers/Rules/`
  (`ProductCreatedRule`, `ProductUpdatedRule`, `ProductDeletedRule`, `ProductSyncedRule`, replacing
  `CatalogProductChangedRule`/`CatalogSearchSyncRule`).
- `src/Services/Pricing/Pricing.Function` — `Modules/Prices/EventsIntegration/Consumers/ProductDeleted/`
  (renamed from `CatalogProductRemoved`, no more in-handler `ChangeType` guard).
- `src/Services/CatalogView/CatalogView.Function` — `Modules/Products/EventsIntegration/Consumers/`
  (`ProductSync` now upsert-only; new `ProductDeleted` consumer).
- `src/BuildingBlocks/BuildingBlocks.Messaging/Events` — `ProductCreatedEvent`, `ProductUpdatedEvent`,
  `ProductDeletedEvent`, `ProductSyncedEvent`, replacing `CatalogUpdatedEvent`/`CatalogProductSyncEvent`.
- `src/WebApps/Shopping.Web.SPA.React` — the `revalidator` Lambda subscribes to the three named
  product events instead of one generic one. **Updated by
  [ADR-0035](./0035-catalogview-owned-cdc-events-drive-spa-revalidation.md)**: the product/price/
  rating path now subscribes to CatalogView's own `CatalogViewProductSyncedEvent`/
  `CatalogViewProductDeletedEvent` instead of Catalog's `ProductCreatedEvent`/
  `ProductUpdatedEvent`/`ProductDeletedEvent` directly (still one named event per occurrence, per
  this ADR's rules — just emitted one layer downstream, by CatalogView instead of Catalog); the
  `ReviewCreatedEvent`/`ReviewUpdatedEvent` subscription for the `reviews:{id}` tag is unchanged.

---

## Consequences

### Positive

- **Consumers self-document what they react to.** A subscription to `ProductDeletedEvent` says
  exactly what triggers it — no need to read the handler body to learn it silently discards every
  non-REMOVE change.
- **Adding an occurrence is adding a rule, not touching existing consumers.** A future
  `ProductArchivedEvent` (say) is a new `IStreamRule` and a new EventBridge subscription; nothing
  that already consumes `ProductDeletedEvent` needs to change.
- **No wasted invocations on irrelevant occurrences.** Pricing's consumer no longer receives (and
  discards) every create/update — its EventBridge rule only matches `ProductDeletedEvent`.
- **Consistent with the codebase's own precedent.** `OrderCreatedRule`/`ReviewCreatedRule`/
  `ReviewUpdatedRule`/`CatalogCategorySyncRule` already worked this way; Catalog's product events
  now match the rest of the system instead of being the one exception.

### Negative / Costs

- **More event types and CDK rules per aggregate.** Four events/rules replace two — more files to
  navigate, more EventBridge `Rule` resources to provision.
- **A shared thin event needs discipline.** `ProductDeletedEvent` is intentionally reused by two
  consumers; it must stay occurrence-only (id + nothing else) — if a future consumer needs more
  than the id on delete, that is a new, separate event, not a field bolted onto this one.

### Mitigation Strategies

- Keep shared thin events (`ProductDeletedEvent`) to a single primitive field (the id) by
  convention — any consumer needing more must justify a new, purpose-built event rather than
  widening a shared one back into a multi-purpose payload.
- The `service-architecture` skill documents this pattern (see below) so new publishers follow it
  by default instead of reaching for a single any-change rule out of convenience.

### Future Constraints

- A CDC integration event MUST NOT carry a field mirroring DynamoDB Streams' `EventName`
  vocabulary (`INSERT`/`MODIFY`/`REMOVE`) for a consumer to branch on.
- A stream-rule publisher reacting to 2+ distinct domain occurrences MUST split into one
  `IStreamRule<TImage>` per occurrence, each emitting its own named event.
- A consumer MUST subscribe to only the detail-types it needs; it MUST NOT filter out irrelevant
  occurrences in-handler when the EventBridge rule could exclude them instead.

---

## Related Documentation

- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) (amended — see "Amends ADR-0019 §4" above)
- [ADR-0005: Remove Domain Events; CDC via DynamoDB Streams](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0011: Review Bounded Context — Rating Aggregation via CDC](./0011-review-bounded-context-rating-aggregation-via-cdc.md)
- [ADR-0029: Review Upsert Composite Key and Rating Delta](./0029-review-upsert-composite-key-and-rating-delta.md) (the `ReviewCreatedEvent`/`ReviewUpdatedEvent` precedent)
- [ADR-0026: Pricing Bounded Context — Price and Campaign Ownership](./0026-pricing-bounded-context-price-and-campaign-ownership.md)
- [ADR-0027: CatalogView OpenSearch Product Search and Rating Sync](./0027-catalogview-opensearch-product-search-and-rating-sync.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)

## References

- `.claude/skills/service-architecture/SKILL.md` §7 — updated with this ADR's constraint.
- `src/BuildingBlocks/BuildingBlocks.Messaging/Streams/{IStreamRule,StreamRuleDispatcher,StreamContext}.cs`
