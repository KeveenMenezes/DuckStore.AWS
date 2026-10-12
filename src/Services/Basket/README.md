# Basket

Owns the shopping cart. Identity is resolved **server-side**, and the context has zero responsibility
for discounts.

## Architecture

![Basket architecture](../../../docs/diagrams/basket.svg)

<sub>Source: [`docs/duckstore-process-flow.drawio`](../../../docs/duckstore-process-flow.drawio), page **Basket**. Regenerate with `./scripts/export-diagrams.sh`.</sub>

## Responsibilities

- Owns `shopping-carts`; `ShoppingCart` is the aggregate root.
- Merges a guest cart into the user cart at login.
- Closes the cart for the checkout saga — atomically and at most once per checkout attempt — and
  emits the checkout event that starts the order and payment flows
  ([ADR-0050](../../../docs/adr/0050-checkout-step-functions-express-saga.md)).

### What this context deliberately does not do

- **No discount logic.** `Coupon`, `DynamoCouponRepository` and the `coupons` table were removed;
  discount and campaign ownership belongs to Pricing
  ([ADR-0026](../../../docs/adr/0026-pricing-bounded-context-price-and-campaign-ownership.md) ·
[ADR-0050](../../../docs/adr/0050-checkout-step-functions-express-saga.md),
  superseding ADR-0012). Basket never calls Pricing — neither synchronously nor asynchronously.
- **No caching layer.** Redis and DAX were removed; the repository talks straight to DynamoDB
  ([ADR-0013](../../../docs/adr/0013-remove-basket-caching-redis-and-dax.md)).
- **No say over the order's contents at checkout.** The checkout saga hands Basket the order lines
  already priced by Pricing and named by Catalog, and the `OrderId` it derived; Basket never re-reads
  the cart and never trusts the price or name the client stored in it.
- **No card data.** The card goes to Payment, never through Basket or `BasketCheckoutEvent`.

## Data

| Table | Key | Stream | Notes |
|---|---|---|---|
| `shopping-carts` | PK `OwnerId` | `NEW_IMAGE` | TTL on `ExpiresAt` — expires guest carts and `CHECKOUT#` records |

`OwnerId` is a server-resolved prefixed key: `USER#<cognito-sub>` for an authenticated cart or
`GUEST#<guestId>` for a visitor. **Identity never comes from the client**
([ADR-0016](../../../docs/adr/0016-guest-basket-owner-id-identity-api-key-and-ttl.md)).

Every cart write stores a fresh `Version` token. A third, server-only key namespace holds one record
per closed checkout attempt, `CHECKOUT#<ownerId>#<checkoutId>`, with the `OrderId` it created and a
24 h TTL: it is what makes a retried checkout return the same order.

## API surface (AppSync)

| Field | Resolver |
|---|---|
| `Query.basket` | Direct DynamoDB |
| `Mutation.storeBasket` · `deleteBasket` | Direct DynamoDB |
| `Mutation.checkoutBasket` | HTTP data source → `checkout-saga` Step Functions Express state machine ([ADR-0050](../../../docs/adr/0050-checkout-step-functions-express-saga.md)): replay check → saved cart → Catalog lines → Pricing quote → `payment-register` → `basket-checkout-basket` (one `TransactWriteItems`: Put the `CHECKOUT#` record, Delete the cart only if its `Version` is the one quoted; `CartChanged` otherwise) → `payment-void` only on `CartChanged`. The Lambda has no Function URL and is invoked only by the saga |
| `Mutation.mergeBasket` | Lambda — `basket-merge-basket` |

## Integration events

**Publishes** — `basket-shopping-carts-stream-publisher`, off the `shopping-carts` stream. The
INSERT of a `CHECKOUT#` record (`Type=Checkout`) is what emits:

- `BasketCheckoutEvent` — consumed **in parallel** by Ordering, Payment and Challenges. It carries
  the payment method and installments, never the card

**Consumes** — nothing.

## Failure handling

`basket-dlq` receives the publisher's `SqsDlq` after bisect + retries. Non-empty →
`basket-dlq-not-empty` → `duckstore-alerts`
([ADR-0015](../../../docs/adr/0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md)).

## Local development

```bash
dotnet run --project src/AppHost/AppHost.csproj
dotnet test tests/Services/Basket/Basket.UnitTests/Basket.UnitTests.csproj
```

The checkout flow end to end, against a deployed environment, is covered by
[`tests/E2E/Checkout`](../../../tests/E2E/README.md).

## Related ADRs

[ADR-0005](../../../docs/adr/0005-remove-domain-events-cdc-via-dynamodb-streams.md) ·
[ADR-0013](../../../docs/adr/0013-remove-basket-caching-redis-and-dax.md) ·
[ADR-0016](../../../docs/adr/0016-guest-basket-owner-id-identity-api-key-and-ttl.md) ·
[ADR-0026](../../../docs/adr/0026-pricing-bounded-context-price-and-campaign-ownership.md)
