# Payment

Simulates payment authorization for orders. Two bounded contexts share this stack — **Payment**, which
owns the payment record, and **PaymentGateway**, the simulated external processor.

## Architecture

![Payment architecture](../../../docs/diagrams/payment.svg)

<sub>Source: [`docs/duckstore-process-flow.drawio`](../../../docs/duckstore-process-flow.drawio), page **Payment**. Regenerate with `./scripts/export-diagrams.sh`.</sub>

## Responsibilities

- **Payment** owns `payments`, creating a record from checkout and applying the gateway's outcome.
- **PaymentGateway** ([`../PaymentGateway`](../PaymentGateway)) decides authorize/decline. It is
  stateless: in the CDK it receives only `eventBus.grantPutEventsTo(...)` and **no DynamoDB grant**.

## The full cycle

The two EventBridge icons on the diagram are the same bus — the round trip runs through it.

1. `BasketCheckoutEvent` → creates the `Payment` (Ordering consumes the same event in parallel)
2. CDC publishes `PaymentRequestedEvent`
3. The gateway decides and publishes `PaymentAuthorizedEvent` / `PaymentDeclinedEvent`
4. Payment's own consumers apply the outcome to the `payments` table
5. Ordering consumes that same outcome independently

### When the checkout used points

The payment must not reach the gateway before the points are actually reserved, or it could be
authorized with a discount the customer didn't have
([ADR-0048 §6](../../../docs/adr/0048-points-transactions-ledger-and-in-cart-redemption.md)):

1. A checkout with `PointsToUse > 0` creates the payment `AwaitingPoints`, not `Pending` — the INSERT
   publishes nothing. Challenges consumes the same `BasketCheckoutEvent` and reserves the points.
2. `PointsReservedEvent` → `payment-points-reserved-consumer` moves it `AwaitingPoints → Pending`;
   `PaymentRequestedRule` matches that MODIFY, and the cycle above continues from step 2.
3. `PointsReservationFailedEvent` → `payment-points-reservation-failed-consumer` declines it
   (`InsufficientPoints`); `PaymentDeclinedInsufficientPointsRule` publishes `PaymentDeclinedEvent`
   so Ordering cancels the order, and Payment's own result consumer ignores it (no longer `Pending`).

Both consumers **throw** when the payment does not exist yet — the two contexts consume the checkout
in parallel, so the reservation can arrive first, and EventBridge retries.

## Data

| Table | Key | Stream | Notes |
|---|---|---|---|
| `payments` | PK `Id`, GSI1 `ORDER#<orderId>` (ALL) | `NEW_AND_OLD_IMAGES` | Drives the CDC publisher; GSI1 finds the payment of an order |
| `payment-processed-events` | PK `PK` | — | Idempotency inbox, written by all five Payment consumers |

## API surface (AppSync)

None. This context is reached only through events.

## Integration events

**Consumes**

| Rule | Event |
|---|---|
| `payment-basket-checkout-consumer-rule` | `BasketCheckoutEvent` |
| `payment-result-authorized-consumer-rule` | `PaymentAuthorizedEvent` |
| `payment-result-declined-consumer-rule` | `PaymentDeclinedEvent` |
| `payment-points-reserved-consumer-rule` | `PointsReservedEvent` (Challenges) |
| `payment-points-reservation-failed-consumer-rule` | `PointsReservationFailedEvent` (Challenges) |
| `paymentgateway-payment-requested-consumer-rule` | `PaymentRequestedEvent` |

**Publishes**

- `PaymentRequestedEvent` — via `payment-payments-stream-publisher` (CDC off `payments`), on INSERT
  `Pending` or MODIFY `AwaitingPoints → Pending`
- `PaymentDeclinedEvent` — the same publisher, on MODIFY `AwaitingPoints → Declined` (not enough points)
- `PaymentAuthorizedEvent` · `PaymentDeclinedEvent` — from the gateway consumer

Downstream, `PaymentAuthorizedEvent` is also consumed by Challenges, which marks the order's reserved
points `Used`; it carries `CustomerId` for that.

## Failure handling

**Two DLQs, one per bounded context** — `payment-dlq` and `paymentgateway-dlq`. Keeping them separate
means a gateway failure never hides behind Payment's own backlog. Both alarm into `duckstore-alerts`
([ADR-0015](../../../docs/adr/0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md)).

## Local development

```bash
dotnet run --project src/AppHost/AppHost.csproj
dotnet test tests/Services/Payment/Payment.UnitTests/Payment.UnitTests.csproj
```

## Related ADRs

[ADR-0005](../../../docs/adr/0005-remove-domain-events-cdc-via-dynamodb-streams.md) ·
[ADR-0015](../../../docs/adr/0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md) ·
[ADR-0025](../../../docs/adr/0025-payment-bounded-context-simulated-gateway-cdc.md) ·
[ADR-0048](../../../docs/adr/0048-points-transactions-ledger-and-in-cart-redemption.md)
