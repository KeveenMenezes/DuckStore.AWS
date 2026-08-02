---
tags:
  - status/accepted
  - domain/catalogview
---

# ADR-0040: CatalogView Consumer Lambdas Consolidated by Producer Bounded Context via Strategy Dispatch

## Status
**Accepted** — July 2026

**Amended — July 2026 by [ADR-0044](./0044-campaign-cdc-product-discounts-stream-and-ttl.md).** §1's
allowance for `PriceChangedEvent` was conditional on Pricing having a single occurrence.
ADR-0044 added `ProductDiscountChangedEvent`, which is exactly the trigger this ADR's own Future
Constraints name, so Pricing now has its own `IPricingSyncStrategy`/`PricingSyncDispatcher` pair and
`catalogview-price-sync-consumer` became `catalogview-pricing-sync-consumer`. §1 is retained below as
the record of why the 1:1 handler was correct while it held; every other section stands unchanged.

---

## Context

CatalogView (ADR-0030) keeps `catalogview-products` in sync via **six** independent Lambdas, each
wired 1:1 to a single named CDC event (ADR-0031):

| Lambda | Detail-type | Producer bounded context |
|---|---|---|
| `catalogview-product-sync-consumer` | `ProductSyncedEvent` | Catalog |
| `catalogview-product-deleted-consumer` | `ProductDeletedEvent` | Catalog |
| `catalogview-category-sync-consumer` | `CatalogCategorySyncEvent` | Catalog |
| `catalogview-review-aggregate-consumer` | `ReviewCreatedEvent` | Review |
| `catalogview-review-update-aggregate-consumer` | `ReviewUpdatedEvent` | Review |
| `catalogview-price-sync-consumer` | `PriceChangedEvent` | Pricing |

This shape is a direct consequence of ADR-0031's core rule — one named event per domain
occurrence, no `ChangeType` discriminator — but ADR-0031 does **not** require one Lambda per
event. Its Decision §4 states rules explicitly in terms of *"one EventBridge rule per detail-type
per Lambda"*, and its own Context section cites a precedent already living in this codebase: the
SPA revalidator subscribes to **both** `ReviewCreatedEvent` and `ReviewUpdatedEvent` on a single
`Bus.subscribe`/Lambda — proof that "one Lambda, several named detail-types, one rule each" already
works end-to-end here.

`CatalogViewLambdas` never applied that precedent to itself: `reviewAggregateConsumer` and
`reviewUpdateAggregateConsumer` are the exact `ReviewCreatedEvent`/`ReviewUpdatedEvent` pair ADR-0031
cites as the model case, yet they ship as two separate Lambdas, two separate `DockerImageFunction`
resources, two separate DLQ wiring blocks — for no reason tied to the event contract itself.

Six Lambdas sharing one Docker image (`catalogViewImage` in `infra/constructs/catalogview-lambdas.ts`)
also means the "cost" of a separate Lambda here is not really about isolating deployable units — the
build is already one artifact. What six Lambdas actually buys today is CDK boilerplate (six
`DockerImageFunction` blocks, six `ruleFor(...)` calls, six DLQ/async-invoke configurations) without
a matching reduction in coupling: `product-sync`, `product-deleted`, and `category-sync` are already
coupled by construction — they read the same Catalog write path, target the same table, and are
already deployed together as part of the same image build.

## Decision

CatalogView's write-consumer Lambdas are grouped by **producer bounded context** instead of by
individual event, using a **Strategy** dispatch pattern inside each grouped Lambda to keep the
per-event write boundary explicit and testable in isolation:

| Lambda | Detail-types handled | Strategies |
|---|---|---|
| `catalogview-catalog-sync-consumer` | `ProductSyncedEvent`, `ProductDeletedEvent`, `CatalogCategorySyncEvent` | `ProductSyncStrategy`, `ProductDeleteStrategy`, `CategorySyncStrategy` |
| `catalogview-review-sync-consumer` | `ReviewCreatedEvent`, `ReviewUpdatedEvent` | `ReviewCreateStrategy`, `ReviewUpdateStrategy` |
| `catalogview-price-sync-consumer` | `PriceChangedEvent` | *(unchanged — stays a plain handler)* |

`catalogview-products-stream-publisher` (ADR-0035, DynamoDB Streams-triggered, CDC **out** of
CatalogView) is unaffected — this ADR only concerns the CDC **consumers**.

### 1. `PriceChangedEvent` stays a plain 1:1 handler

> **No longer in force — superseded by [ADR-0044](./0044-campaign-cdc-product-discounts-stream-and-ttl.md) §4.**
> Pricing gained a second occurrence (`ProductDiscountChangedEvent`), so the condition this section
> rests on no longer holds and it now has a strategy pair like Catalog and Review. The reasoning
> below is retained because it remains the correct test for *when* a group of one is justified — it
> was not wrong, its premise expired.

Pricing is the only producer with a single occurrence relevant to CatalogView. Consolidating a
group of one buys nothing and would misrepresent this ADR's own rule: grouping is justified by
**2+ occurrences from the same producer**, not by producer identity alone. This mirrors ADR-0019
§4's original allowance ("a single unconditional trigger MAY stay a plain handler"), which
ADR-0031 narrowed but did not remove.

### 2. Grouped Lambdas dispatch via a per-producer strategy contract, never an in-handler `switch`

The failure mode ADR-0031 rejected was a `ChangeType` **field** consumers branched on inside one
handler body, hiding what the consumer actually reacted to. Grouping by producer must not
reintroduce that shape by another name. Instead, **each producer group gets its own strategy
interface and dispatcher** — `ICatalogSyncStrategy`/`CatalogSyncDispatcher` for Catalog,
`IReviewSyncStrategy`/`ReviewSyncDispatcher` for Review — rather than one interface shared across
producers. A strategy contract only exists where 2+ occurrences from the same producer justify it
(§1); there is deliberately no single `ISyncStrategy` spanning every producer, so a Catalog
strategy can never be registered where a Review dispatcher would resolve it, and vice versa — the
boundary is enforced by the type system, not just by `CanHandle`'s string match:

```csharp
// Modules/Products/EventsIntegration/Consumers/CatalogSync/ICatalogSyncStrategy.cs
public interface ICatalogSyncStrategy
{
    bool CanHandle(string detailType);

    Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default);
}

// Modules/Products/EventsIntegration/Consumers/CatalogSync/CatalogSyncDispatcher.cs
public sealed class CatalogSyncDispatcher(IEnumerable<ICatalogSyncStrategy> strategies)
{
    public Task DispatchAsync(EventBridgeEvent<JsonElement> evt, CancellationToken cancellationToken = default)
    {
        var strategy = strategies.SingleOrDefault(s => s.CanHandle(evt.DetailType))
            ?? throw new InvalidOperationException(
                $"No Catalog sync strategy registered for detail-type '{evt.DetailType}'.");

        return strategy.HandleAsync(evt.Id, evt.Detail, cancellationToken);
    }
}
```

`IReviewSyncStrategy`/`ReviewSyncDispatcher` are the identical shape, one producer over. Each
strategy is exactly today's handler, unchanged in behavior, just implementing its producer's
interface and deserializing its own typed event from the raw `JsonElement`:

```csharp
public sealed class ProductSyncStrategy(IProductSearchIndex index) : ICatalogSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ProductSyncedEvent);

    public Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default) =>
        index.UpsertAsync(ToDocument(detail.Deserialize<ProductSyncedEvent>()!), cancellationToken);

    private static SearchDocument ToDocument(ProductSyncedEvent evt) => new() { /* unchanged */ };
}
```

This is structurally the same shape as the CDC-publishing side's existing
`IStreamRule<TImage>`/`StreamRuleDispatcher<TImage>` (ADR-0019) — CatalogView already has this
pattern for outbound events; this ADR applies the same idea to inbound consumption. Unlike
`IStreamRule<TImage>`, which is generic over the stream image type, the two strategy contracts here
are not made generic over their producer — each producer gets its own named interface instead of
`ISyncStrategy<TProducer>`, keeping DI registration (`AddScoped<ICatalogSyncStrategy, ...>`) and the
dispatcher's constructor (`IEnumerable<ICatalogSyncStrategy>`) unambiguous about which group a
strategy belongs to.

### 3. The write boundary lives in `IProductSearchIndex`, not in the strategy

Grouping consumers must not weaken the "a consumer only writes the fields it owns" guarantee
(ADR-0027 §3, ADR-0030). That guarantee already lives one layer down, in
`IProductSearchIndex`/`DynamoProductIndex` — `UpsertAsync`'s partial merge never touches
`Price`/`Rating*`, `ApplyPricingAsync` only merges price/payment fields, etc. Strategies MUST keep
delegating to those narrow methods; a strategy MUST NOT take a raw `SearchDocument`/full item write
as a shortcut. Grouping by producer changes only *how many Lambdas exist and how they dispatch* —
it changes nothing about which fields any given code path is allowed to write.

### 4. One EventBridge rule per detail-type, all targeting the grouped Lambda

Per ADR-0031 §4, each detail-type keeps its own `events.Rule` (own `ruleName`, own
`eventPattern.detailType`) — only the `targets.LambdaFunction` changes, from a dedicated Lambda to
the shared grouped one. This preserves ADR-0031's requirement that a consumer subscribes only to
the detail-types it needs; nothing here reverts to a broad "any change" rule.

## Applies To

- `src/Services/CatalogView/CatalogView.Function/Modules/Products/EventsIntegration/Consumers/` —
  new `CatalogSync/` and `ReviewSync/` folders (each with `Endpoint.cs` + `Strategies/`), replacing
  `ProductSync/`, `ProductDeleted/`, `CategorySync/`, `ReviewCreated/`, `ReviewUpdated/`.
  `PriceChanged/` is unchanged.
- `src/Services/CatalogView/CatalogView.Function/Shared/Configuration/ServiceRegistration.cs` —
  registers `ICatalogSyncStrategy`/`CatalogSyncDispatcher` and `IReviewSyncStrategy`/
  `ReviewSyncDispatcher` instead of five standalone handler types.
- `infra/constructs/catalogview-lambdas.ts` — three `DockerImageFunction`s for consumers (down
  from five; `productStreamPublisher` unaffected) with multiple `ruleFor(...)` calls per grouped
  Lambda.
- `infra/stacks/catalogview-stack.ts` — `CfnOutput`s renamed to match the grouped Lambdas.
- `src/AppHost/CatalogViewExtensions.cs` — `AddAWSLambdaFunction<...>` calls reduced to match,
  local Aspire `lambdaHandler` strings updated to the new generated method names.
- `tests/Services/CatalogView/CatalogView.UnitTests/Products/` — handler tests become strategy
  tests (`ProductSyncStrategyTests`, etc.), plus `CatalogSyncDispatcherTests`/
  `ReviewSyncDispatcherTests`.

## Consequences

### Positive

- **Fewer CDK resources for the same behavior.** Five `DockerImageFunction`/DLQ/async-invoke blocks
  become three; `ruleFor(...)` calls stay one-per-detail-type, so no observability granularity is
  lost at the EventBridge rule level — only at the compute-resource level, where it was redundant
  anyway (all six already shared one Docker image).
- **Completes a pattern the codebase already endorsed but didn't apply to itself.** The
  `ReviewCreatedEvent`/`ReviewUpdatedEvent` pair is the literal precedent ADR-0031 cites for "one
  Lambda, several detail-types" — this ADR is that precedent, finally applied to the Lambda that
  motivated it.
- **The write boundary becomes an explicit, named type** (an `ICatalogSyncStrategy`/
  `IReviewSyncStrategy` implementation per event) instead of an implicit guarantee that lived only
  in `IProductSearchIndex` method boundaries — easier for a new contributor to see "this is the
  full list of things that write to `catalogview-products`, and what each one is allowed to touch."
  Splitting the contract per producer also means a Catalog strategy is structurally impossible to
  register on the Review dispatcher (or vice versa) — the compiler enforces the producer boundary,
  not just each strategy's own `CanHandle` check.
- **Mirrors an existing pattern in the same codebase** (`IStreamRule<TImage>`/
  `StreamRuleDispatcher<TImage>`), so there's no new mental model to learn — just the same
  match-and-dispatch idea applied to the inbound side.

### Negative / Costs

- **Shared blast radius on deploy/cold start within a group.** A bad deploy or DI misconfiguration
  in `catalogview-catalog-sync-consumer` now risks all three Catalog-sourced detail-types at once,
  not just one. Accepted because the three were already coupled (same producer, same table, same
  image) and already deploy together.
- **IAM grants widen slightly.** `product-deleted-consumer` today holds only `grantWriteData`;
  grouped into `catalog-sync-consumer`, it inherits the group's `grantReadWriteData` (needed by
  `ProductSyncStrategy`/`CategorySyncStrategy`). A minor loss of least-privilege, scoped to a single
  table this Lambda already needs read+write access to for its other two strategies.
- **Dispatch adds one indirection.** `[LambdaFunction]` methods no longer bind a single strongly
  typed `EventBridgeEvent<TDetail>` — they accept `EventBridgeEvent<JsonElement>` and each strategy
  deserializes its own detail type. Slightly more code than the previous 1:1 binding, in exchange
  for the grouping.
- **Log/trace correlation needs discipline.** Three detail-types now share one CloudWatch log
  group/X-Ray trace stream; strategies must log their own name so a debugging session can tell
  which one handled a given invocation.

### Mitigation Strategies

- Keep strategy implementations one-behavior-each and unit-tested in isolation (mocking
  `IProductSearchIndex`), exactly as today's handlers are — each dispatcher gets a thin test
  verifying it routes each detail-type to the right strategy and throws on an unregistered one.
- Strategies MUST delegate to `IProductSearchIndex`'s existing narrow methods (`UpsertAsync`,
  `DeleteAsync`, `ApplyPricingAsync`, `RenameCategoryAsync`, `ApplyRatingAsync`,
  `ApplyRatingUpdateAsync`) — never construct or write a full `SearchDocument` directly from a
  strategy, preserving the non-clobbering-merge guarantee from ADR-0027 §3/ADR-0030.
- If a future producer needs 2+ occurrences (mirroring today's Catalog/Review shape), group it the
  same way from the start rather than defaulting to a new dedicated Lambda per event.

### Future Constraints

- A new CDC consumer for an **existing** grouped producer (Catalog or Review) MUST add a new
  strategy implementing that producer's interface (`ICatalogSyncStrategy`/`IReviewSyncStrategy`) to
  the existing grouped Lambda plus a new EventBridge rule targeting it — MUST NOT provision a new
  standalone Lambda for that producer.
- A new CDC consumer for a **new** producer with a single occurrence MAY stay a plain 1:1 handler
  (mirroring `PriceChangedEvent`) until/unless a second occurrence from that producer appears — at
  which point it gets its own `I<Producer>SyncStrategy`/`<Producer>SyncDispatcher` pair, not a
  branch bolted onto an existing producer's contract.
- A strategy MUST NOT branch on any field within `detail` that mirrors a persistence-layer
  discriminator (`ChangeType`/`EventName`) — `CanHandle` matches only on the EventBridge envelope's
  `detail-type`, never on payload content. This keeps ADR-0031's core rule intact under the new
  dispatch shape.
- A strategy interface MUST NOT be shared across producers. Each producer group owns its own
  `I<Producer>SyncStrategy` contract, even though the shape is identical — this keeps DI
  registration and dispatch resolution scoped by construction, not by convention.

## Related Documentation

- [ADR-0031: CDC Integration Events Named After the Domain Occurrence, Never a Raw ChangeType Discriminator](./0031-cdc-events-named-after-domain-occurrence-no-changetype-discriminator.md) — this ADR's §4 ("one rule per detail-type per Lambda") and its `ReviewCreatedEvent`/`ReviewUpdatedEvent` precedent are what this ADR generalizes to CatalogView's own consumers.
- [ADR-0030: CatalogView Goes DynamoDB-Backed — Drop OpenSearch](./0030-catalogview-dynamodb-drop-opensearch.md) — the six-consumer shape and the non-clobbering-merge guarantee (§3) this ADR preserves unchanged.
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) — `IStreamRule<TImage>`/`StreamRuleDispatcher<TImage>`, the pattern `ICatalogSyncStrategy`/`CatalogSyncDispatcher` and `IReviewSyncStrategy`/`ReviewSyncDispatcher` mirror on the inbound side.
- [ADR-0027: CatalogView — Product Search and Rating Aggregation via Amazon OpenSearch](./0027-catalogview-opensearch-product-search-and-rating-sync.md) — §3, the non-clobbering-merge guarantee.
- [ADR-0029: Review Upsert Composite Key and Rating Delta](./0029-review-upsert-composite-key-and-rating-delta.md) — `ReviewCreatedEvent`/`ReviewUpdatedEvent` pair grouped by this ADR.
- [ADR-0035: CatalogView-Owned CDC Events Drive SPA Revalidation](./0035-catalogview-owned-cdc-events-drive-spa-revalidation.md) — `productStreamPublisher`, explicitly out of this ADR's scope.

## References

- `infra/constructs/catalogview-lambdas.ts` — current six-Lambda construct, to be reduced to three
  consumer Lambdas + the unchanged stream publisher.
- `src/Services/CatalogView/CatalogView.Function/Modules/Products/Data/IProductSearchIndex.cs` —
  the write-boundary contract strategies keep delegating to, unchanged by this ADR.
- `src/BuildingBlocks/BuildingBlocks.Messaging/Streams/{IStreamRule,StreamRuleDispatcher}.cs` — the
  existing dispatch pattern this ADR mirrors.
