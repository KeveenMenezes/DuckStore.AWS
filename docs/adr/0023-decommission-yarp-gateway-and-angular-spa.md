---
tags:
  - status/accepted
  - domain/spa
  - domain/cross-cutting
---

# ADR-0023: Decommission the YARP Gateway and Complete the Angular SPA Removal

## Status
**Accepted** — July 2026

---

## Context

Two pieces of the pre-AppSync architecture are still in the tree but no longer on any real request
path, and they actively mislead readers of the codebase.

**The YARP gateway is vestigial.** `src/ApiGateways/YarpApiGateway` was the per-service prefix
router of the HTTP-gateway era. Today:

- `appsettings.json` and `appsettings.Development.json` both contain empty `"Routes": {}` and
  `"Clusters": {}` — the proxy routes **nothing**.
- `Program.cs` hardcodes CORS to `http://localhost:4200`, the port of the removed Angular app; the
  React app runs on `:3000`. A rate-limiter policy (`"fixed"`) is defined but attached to no route.
- It is wired into the local Aspire graph (`src/AppHost/Program.cs`) but has **no CDK/SST stack** —
  it is never deployed. On AWS the client path is AppSync (direct resolvers per
  [ADR-0007](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md) /
  [ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)) fronted
  by the Next.js BFF ([ADR-0016](./0016-guest-basket-owner-id-identity-api-key-and-ttl.md)).

**The Angular removal was never finished.** The React/Next.js SPA replaced Angular per
[ADR-0006](./0006-react-nextjs-spa-over-angular.md), and the Angular directory is gone, but:

- `.gitmodules` contains **two entries pointing at the same Angular repository** (root-level
  `DuckStore.Shopping.Web.SPA` and `src/WebApps/Shopping.Web.SPA`), neither checked out.
- `src/AppHost/manifest.json` still references the `../WebApps/Shopping.Web.SPA` build context and
  Dockerfile.
- `README.md` still lists Angular among the technologies; `CLAUDE.md` still documents
  `src/WebApps/Shopping.Web.SPA` as a live app and the YARP gateway as an active per-service
  router.

Dead-but-present infrastructure has a cost even in a learning repo: every reader (and every ADR)
has to re-derive that it is dead, and misconfiguration like the `:4200` CORS suggests changes are
being made around it without it ever being exercised.

---

## Decision

**AppSync + the Next.js BFF is the only client entry point, in every environment.** The YARP
gateway and all remaining Angular artifacts are removed rather than repaired.

### 1. Remove the YARP gateway

- Delete `src/ApiGateways/YarpApiGateway` and remove the project from `DuckStore.sln`.
- Remove its registration and all `WithReference(yarpApiGateway)` wiring from `src/AppHost`.
- No replacement is introduced: locally the SPA's BFF (`app/api/graphql/local.ts`) already talks to
  the services/DynamoDB Local directly, mirroring the AppSync path on AWS. Reintroducing an HTTP
  gateway in the future would require a new ADR, since it would contradict the single-entry-point
  rule established here and in ADR-0007.

### 2. Finish the Angular removal

- `.gitmodules`: delete both Angular submodule entries (and the corresponding `.git/config`
  entries / gitlink stubs if present).
- `src/AppHost/manifest.json`: remove the `Shopping.Web.SPA` resource block.
- `README.md`: drop Angular from the technology list; name React/Next.js as the SPA.
- `CLAUDE.md`: remove the Angular SPA bullet and rewrite the gateway/webapps section to describe
  the AppSync + BFF entry path.

### 3. Status bookkeeping

ADR-0006 (React SPA replacing Angular) remains `Proposed` on its own merits; this ADR completes its
removal clause. No ADR is superseded: ADR-0007/0009/0016 already describe the AppSync/BFF entry
path — this decision removes the last artifacts that contradicted them.

---

## Applies To

- `src/ApiGateways/YarpApiGateway/**` (deleted)
- `DuckStore.sln`
- `src/AppHost/Program.cs` and any `*Extensions.cs` referencing the gateway
- `.gitmodules`
- `src/AppHost/manifest.json`
- `README.md`, `CLAUDE.md`

---

## Consequences

### Positive

- The codebase matches the documented architecture: one client entry point (AppSync/BFF), locally
  and on AWS — no phantom second gateway to reason about, secure, or keep patched.
- Removes actively wrong configuration (empty routes, `:4200` CORS, unused rate limiter) that
  misleads anyone studying the repo.
- Smaller Aspire graph — one fewer project to build and boot on every `dotnet run`.

### Negative / Costs

- If a plain HTTP edge (non-GraphQL) is ever needed again — e.g. webhooks for a third party that
  cannot speak GraphQL — there is no gateway to hang it on. (Today's only webhook, SPA
  revalidation, is already served by a Next.js route handler, not YARP.)
- The rate-limiting example (the one YARP feature with any content) disappears from the codebase.

### Mitigation Strategies

- Rate limiting and HTTP-edge concerns belong to the managed edge in this architecture (AppSync /
  CloudFront / Lambda function URLs with IAM) — if needed, decide it there via a new ADR rather
  than resurrecting a self-hosted proxy.

---

## References

- [ADR-0006: React/Next.js as Primary SPA, Replacing Angular](./0006-react-nextjs-spa-over-angular.md)
- [ADR-0007: AppSync (GraphQL) as Client-Facing API](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0016: Guest Shopping Carts — Unified ownerId, API_KEY, and TTL](./0016-guest-basket-owner-id-identity-api-key-and-ttl.md)
- `src/ApiGateways/YarpApiGateway/Program.cs`, `appsettings.json`
