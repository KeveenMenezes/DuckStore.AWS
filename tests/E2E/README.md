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
| Inventory | `Inventory/Inventory.E2ETests` | Warehouses in the management app (ADR-0052): the pages as StockKeeper, Admin and Seller, and the five resolvers' errors and group gate called on AppSync with each user's own token |

`E2E.Infrastructure` holds what every context shares: settings, the signed-in `StorefrontSession` and the
`[E2EFact]` attribute, and for the Blazor management app `ManagementSettings`, one `ManagementSession` per
staff group (`StockKeeperSession`, `SellerSession`, `AdminSession`) and `[ManagementE2EFact]`.

## Running

The suites skip themselves unless these are set, so `dotnet test DuckStore.slnx` (and CI) never reach a
deployed environment by accident:

| Variable | Value |
|---|---|
| `E2E_BASE_URL` | The SPA's URL, e.g. `https://duckstore.dev.keveenmenezes.com` |
| `E2E_USER_EMAIL` / `E2E_USER_PASSWORD` | A dedicated test customer in the environment's Cognito user pool. Never commit them |
| `E2E_MANAGEMENT_URL` | The management app's URL, e.g. `https://management-duckstore.dev.keveenmenezes.com` (Inventory) |
| `E2E_{STOCKKEEPER,SELLER,ADMIN}_{EMAIL,PASSWORD}` | One test user per staff group in the management user pool (Inventory). Never commit them |
| `E2E_HEADED` | `1` to watch the browser |
| `E2E_SLOWMO_MS` | Pause between browser actions, e.g. `600`, to follow a headed run |
| `E2E_TRACE_DIR` | Save a Playwright trace per test class there; open with `npx playwright show-trace <file>` |
| `E2E_SCREENSHOT_DIR` | Save a screenshot at each moment a test marks with `CaptureAsync` |

```bash
export E2E_BASE_URL=... E2E_USER_EMAIL=... E2E_USER_PASSWORD=...
dotnet test tests/E2E/Checkout/Checkout.E2ETests
dotnet test tests/E2E/Checkout/Checkout.E2ETests --filter Category=Latency --logger "console;verbosity=detailed"
```

On a dev machine the values live outside the repo, one file per context — `~/.config/duckstore/e2e.env`
(the storefront customer) and `~/.config/duckstore/e2e-inventory.env` (the staff users), both `chmod 600`:

```bash
source ~/.config/duckstore/e2e.env && source ~/.config/duckstore/e2e-inventory.env
dotnet test tests/E2E/Inventory/Inventory.E2ETests
```

The Inventory suite creates warehouses with an `E2E-` code and deactivates them at the end of each test;
warehouses are never deleted (ADR-0052 §2), so they stay on dev as inactive history.

The first run downloads Chromium (Playwright). Every run places real orders for the test customer — run
the suites against dev, never production.
