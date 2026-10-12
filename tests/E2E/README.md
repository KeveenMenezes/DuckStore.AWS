# End-to-end tests

Suites that exercise a **deployed** environment the way a customer does: a real browser signs in through
the SPA's BFF and Cognito's managed login, and the tests drive the storefront UI or call the SPA's
`/api/graphql` with that session. Nothing is mocked — the BFF, AppSync, the Step Functions sagas and the
Lambdas behind them all run for real.

Unlike `tests/Services` (one project per service), suites here are grouped **by business context**,
because a context's flow crosses services:

| Context | Project | Covers |
|---|---|---|
| Checkout | `Checkout/Checkout.E2ETests` | `checkoutBasket` and the checkout saga (ADR-0050): card, cash, replay of an attempt, `PriceChanged`, `InvalidCard`, `BadRequest`, latency |

`E2E.Infrastructure` holds what every context shares: settings, the signed-in `StorefrontSession` and the
`[E2EFact]` attribute.

## Running

The suites skip themselves unless these are set, so `dotnet test DuckStore.slnx` (and CI) never reach a
deployed environment by accident:

| Variable | Value |
|---|---|
| `E2E_BASE_URL` | The SPA's URL, e.g. `https://duckstore.dev.keveenmenezes.com` |
| `E2E_USER_EMAIL` / `E2E_USER_PASSWORD` | A dedicated test customer in the environment's Cognito user pool. Never commit them |
| `E2E_HEADED` | `1` to watch the browser |

```bash
export E2E_BASE_URL=... E2E_USER_EMAIL=... E2E_USER_PASSWORD=...
dotnet test tests/E2E/Checkout/Checkout.E2ETests
dotnet test tests/E2E/Checkout/Checkout.E2ETests --filter Category=Latency --logger "console;verbosity=detailed"
```

The first run downloads Chromium (Playwright). Every run places real orders for the test customer — run
the suites against dev, never production.
