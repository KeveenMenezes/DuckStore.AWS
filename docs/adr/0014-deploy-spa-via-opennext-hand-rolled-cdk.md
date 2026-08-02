---
tags:
  - status/superseded
  - domain/spa
---

# ADR-0014: Deploy the React SPA to AWS via OpenNext, Hand-Rolled CDK

## Status
**Superseded** — July 2026. See [ADR-0020: Migrate the SPA Deploy Layer from Hand-Rolled CDK to SST](./0020-migrate-spa-deploy-to-sst.md).

---

## Context

`Shopping.Web.SPA.React` (ADR-0006) has never been deployed to AWS. `.github/workflows/deploy-spa-cdk.yml`
existed as an empty placeholder, and `.github/workflows/deploy-spa.yml` deploys the **frozen Angular
app**, not the React SPA — no CloudFormation stack for a React SPA has ever existed
(`duckstore-spa-production` does not exist). The SPA now talks to the deployed AppSync API
(ADR-0007) through a BFF proxy, with real Cognito auth, so it is ready to run for real.

Getting there surfaced three concrete problems, in order:

- **The app couldn't produce a production build at all.** `/` and `/products/[id]` use ISR
  (`revalidate=false`, invalidated only by the catalog/review webhooks) and fetch server-side via
  `api/endpoint-resolver.ts`, which resolved to `${NEXT_PUBLIC_SITE_URL}/api/graphql` — the SPA
  calling **itself** over HTTP. During `next build`'s static-generation pass no server is listening
  yet, so every such fetch failed with `ECONNREFUSED` and the build never succeeded, independent of
  Next.js version or deployment target.
- **`@opennextjs/aws` (the OpenNext AWS adapter) requires Next.js `>=15.5.18 <16 || >=16.2.6`.** The
  SPA was on 16.1.6, squarely in the excluded range.
- **No AWS infrastructure exists to run a Next.js server at all** — Lambda, static asset hosting,
  ISR cache, and a custom domain all had to be designed from scratch.

Next.js's own ISR design (a shared incremental cache + tag-based revalidation across concurrent
Lambda invocations) is exactly the problem the OpenNext AWS adapter (`@opennextjs/aws`) exists to
solve, so it was picked deliberately, not just because it was asked for: plain `next build` +
one Lambda per request would have no way to share revalidation state between invocations, breaking
the webhook-driven ISR strategy ADR-0006 already committed to.

---

## Decision

### 1. Fix the self-loopback before touching infrastructure

Server-side GraphQL calls (Server Components, Route Handlers, and the shared `gql` client) now
talk to AppSync **directly** when `APPSYNC_URL` is configured, instead of looping back through the
SPA's own `/api/graphql`. `api/endpoint-resolver.ts` returns `APPSYNC_URL` directly for server-side
callers in that case; `api/auth-provider.ts`'s `getAuthHeaders()` (renamed from `getAuthToken()`)
resolves the same `Authorization: Bearer <access_token>` / `x-api-key` fallback logic used by the
BFF, so `app/api/graphql/appsync.ts` now just calls `getAuthHeaders()` instead of duplicating the
cookie-reading logic. The browser is unaffected — it still only ever calls the relative
`/api/graphql` BFF, which still exists purely for that path. This is a **correctness fix**, not an
optimization: without it, no production build (via OpenNext or anything else) is possible.

### 2. Bump Next.js to 16.2.9

Required to satisfy `@opennextjs/aws`'s peer dependency. Verified with a clean `pnpm build`
and `pnpm build:opennext` after the bump — no other regressions.

### 3. OpenNext (`@opennextjs/aws`), not a community CDK wrapper

`@opennextjs/aws` is installed as a devDependency with a minimal `open-next.config.ts`
(`{ default: {} }`) and a `build:opennext` script (`open-next build`), producing `.open-next/`
(server function, image optimization function, revalidation function, warmer, static assets,
initial cache seed, and `open-next.output.json` describing exactly how they fit together).

The community construct `cdklabs/cdk-nextjs` (`NextjsGlobalFunctions`) was evaluated to avoid
hand-rolling the CDK wiring, but **rejected after inspecting its installed source**: it does not
use `@opennextjs/aws` at all — it runs plain `next build` in `output: standalone` mode and
implements its own Lambda/cache/revalidation layer. Using it would mean silently switching away
from OpenNext, which was explicitly requested. The CDK infrastructure below is therefore hand-rolled
directly against the real `.open-next/` build output, with every env var name and the DynamoDB
schema confirmed by reading the actual bundled Lambda code in `.open-next/*-function/index.mjs` and
cross-checked against OpenNext's reference implementation
(https://opennext.js.org/aws/reference-implementation) — not guessed from the (fairly vague) public
docs alone.

### 4. Infrastructure shape

| Construct | Resources | Purpose |
|---|---|---|
| `infra/constructs/spa-storage.ts` | 1 S3 bucket (`_assets` + `_cache` prefixes), DynamoDB tag-cache table (PK `tag`, SK `path`, GSI `revalidate`: PK `path`/SK `revalidatedAt`), SQS FIFO revalidation queue | Static assets, ISR incremental cache, and the tag/revalidation bookkeeping OpenNext's server function needs |
| `infra/constructs/spa-lambdas.ts` | 3 Lambda functions (`default` server, image optimizer, revalidation) + SQS event source | Runs the OpenNext bundles; env vars (`CACHE_BUCKET_NAME`, `CACHE_BUCKET_REGION`, `CACHE_BUCKET_KEY_PREFIX`, `CACHE_DYNAMO_TABLE`, `REVALIDATION_QUEUE_URL`, `REVALIDATION_QUEUE_REGION`, `BUCKET_NAME`, `BUCKET_KEY_PREFIX`) match exactly what the bundled code reads |
| `infra/constructs/spa-distribution.ts` | CloudFront distribution (S3 + 2 Lambda Function URL origins, all via Origin Access Control), ACM certificate (us-east-1, DNS-validated), Route53 A/AAAA aliases | Routes exactly per `open-next.output.json`'s `behaviors`: static paths → S3, `_next/image*` → image optimizer, everything else (including `_next/data/*`) → the server function |
| `infra/stacks/spa-stack.ts` | Composes the above | `DuckStoreSpaStack`, following the same props/CfnOutput pattern as `appsync-stack.ts` |

`DuckStoreSpaStack` imports `APPSYNC_URL`/`APPSYNC_API_KEY`/`COGNITO_CLIENT_ID`/`COGNITO_HOSTED_UI_URL`
from `DuckStoreAppSyncStack` via `cdk.Fn.importValue`, rather than duplicating them as separate
GitHub secrets/vars. `CatalogWebhookSecret`/`ReviewWebhookSecret` are `CfnParameter`s (`noEcho`),
matching the pattern `catalog-stack.ts` already uses, passed via `--parameters` in
`.github/workflows/deploy-spa-cdk.yml`.

**Deliberately omitted** from OpenNext's reference architecture: the **warmer function** (keeps the
server function warm to avoid cold starts) and the **dynamodb-provider / initialization function**
(pre-seeds the tag cache from the build's cache manifest on first deploy). Neither affects
correctness — without them, the first requests after a deploy simply render fresh instead of
serving a pre-warmed container or pre-populated cache entry. Their invocation contracts (exact
event/env var shape) aren't documented precisely enough to wire with confidence in this pass; they
are a follow-up, not a gap in this decision.

### 5. Custom domain, one per environment

The SPA gets its own domain under the already-registered `keveenmenezes.com` Route53 hosted zone,
prefixed by environment: `dev-duckstore.keveenmenezes.com` for `dev`. This is what makes
`NEXT_PUBLIC_SITE_URL` (baked in at build time) and the `SPA_BASE_URL`/`SPA_WEBHOOK_URL` GitHub
variables (consumed by `deploy-appsync-cdk.yml` and `deploy-catalog-cdk.yml` respectively) known
**before** the first deploy, instead of only after — no chicken-and-egg bootstrapping step needed.

---

## Applies To

- `src/WebApps/Shopping.Web.SPA.React` — `package.json`, `open-next.config.ts`, `.gitignore`,
  `api/endpoint-resolver.ts`, `api/auth-provider.ts`, `api/graphql-client.ts`, `api/index.ts`,
  `app/api/graphql/appsync.ts`.
- `infra` — `constructs/spa-storage.ts`, `constructs/spa-lambdas.ts`, `constructs/spa-distribution.ts`,
  `stacks/spa-stack.ts`, `bin/app.ts`, `package.json`.
- `.github/workflows/deploy-spa-cdk.yml`.
- Not affected: `src/WebApps/Shopping.Web.SPA` (frozen Angular), `src/WebApps/Shopping.Web.Server`
  (Blazor), any backend `src/Services/*`.

---

## Consequences

### Positive
- The SPA can finally be built and deployed for real; `pnpm build`/`pnpm build:opennext` succeed
  end to end.
- ISR/webhook-driven revalidation (ADR-0006) works correctly across Lambda invocations, because
  OpenNext's S3 + DynamoDB + SQS cache layer is the thing that makes tag invalidation visible
  cluster-wide, not just within one container.
- No dependency on an undocumented/unverified third-party CDK construct's internal behavior —
  every resource and env var in the hand-rolled stack is traceable to either the real bundled code
  or OpenNext's reference implementation.
- Fixed domain per environment removes a whole class of "what URL do I put here before first
  deploy" bootstrapping problems for `SPA_BASE_URL`/`SPA_WEBHOOK_URL`/`NEXT_PUBLIC_SITE_URL`.

### Negative / Costs
- More hand-written CDK than a community construct would have needed — three new constructs plus a
  stack, all of which must be kept in sync manually if OpenNext's output shape changes in a future
  `@opennextjs/aws` upgrade.
- No warmer means cold starts are possible on the server Lambda after idle periods; no cache
  pre-seed means the very first requests after each deploy render fresh rather than from cache.
- The `pnpm` install in `Shopping.Web.SPA.React` now carries `pnpm-workspace.yaml` overrides
  pinning several `@aws-sdk/*`/`@smithy/*` transitive packages to specific older versions, worked
  around a build-environment policy that blocks very-recently-published packages (`@opennextjs/aws`
  exact-pins AWS SDK client versions whose own transitive deps float to "today's newest patch",
  which is always too fresh for that policy). These overrides need periodic revisiting as
  `@opennextjs/aws` itself is upgraded.

### Mitigation Strategies
- Re-derive the CDK wiring from `.open-next/open-next.output.json` (not from memory) whenever
  `@opennextjs/aws` is upgraded — it is the authoritative, build-generated description of what
  origins/behaviors/functions are needed.
- Track the warmer and dynamodb-provider wiring as explicit follow-up work once their invocation
  contracts can be confirmed (e.g. against a newer OpenNext docs revision or the SST source, which
  wires both today).
- Revisit the `pnpm-workspace.yaml` AWS SDK overrides whenever `@opennextjs/aws` is bumped, rather
  than letting them silently drift further from what `@opennextjs/aws` actually ships with.

---

## References
- [ADR-0006: Adopt React/Next.js as the Primary SPA, Replacing Angular](./0006-react-nextjs-spa-over-angular.md)
- [ADR-0007: AppSync GraphQL with Direct DynamoDB Resolvers](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md)
- [ADR-0003: Adoption of Zero Trust Security Model](./0003-adoption-of-zero-trust-security-model.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
- OpenNext AWS reference implementation — https://opennext.js.org/aws/reference-implementation
- `.open-next/open-next.output.json` (generated by `pnpm build:opennext`) — authoritative description
  of the origins/behaviors this ADR's CDK stack implements.
