# ADR-0020: Migrate the SPA Deploy Layer from Hand-Rolled CDK to SST

## Status
**Proposed** — July 2026. Supersedes [ADR-0014](./0014-deploy-spa-via-opennext-hand-rolled-cdk.md).

---

## Context

ADR-0014 deployed the OpenNext-built React SPA (ADR-0006) via a hand-rolled CDK stack
(`infra/stacks/spa-stack.ts` + `infra/constructs/spa-*.ts`), reading `.open-next/` build output
directly and wiring every Lambda/DynamoDB/CloudFront resource by hand. It deliberately omitted two
pieces of OpenNext's reference architecture as "pure performance optimizations": the warmer function
and the **dynamodb-provider cache-seed function**, on the stated grounds that "the app is fully
correct without them" and that the seed Lambda's invocation contract "isn't documented precisely
enough to wire with confidence."

A production debugging session proved that assumption wrong:

- The DynamoDB tag-cache table (`tag`/`path`/`revalidatedAt`, GSI `revalidate`) was **never
  seeded**. A direct table scan in prod returned 0 items, despite the site serving cached ISR pages
  for weeks. `getByTag()`/`getByPath()` — the read path `revalidateTag()` and any on-demand
  revalidation Lambda depend on — had no tag → path mapping to look up, so marking a tag stale was a
  silent no-op. A hand-written `SpaTagRevalidator` Lambda logged `Marked 0 entries stale` on every
  single `CatalogUpdatedEvent`/`ReviewCreatedEvent`, indefinitely.
- The server Lambda was also missing `OPEN_NEXT_BUILD_ID`, an env var OpenNext's bundled DynamoDB
  cache handler (`@opennextjs/aws@^4.0.3`) needs to prefix every tag-cache key
  (`"{buildId}/products"`, not `"products"`) — without it, reads/writes silently target the wrong,
  unprefixed key.
- CloudFront's edge cache (`s-maxage=31536000` on ISR shells, confirmed via response headers —
  `x-cache: Hit from cloudfront` on the second request) was never invalidated when a tag was marked
  stale, so even a correctly-marked tag would never reach a viewer whose edge PoP already had the
  page cached.

Fixing this by hand required decompiling OpenNext's minified Lambda bundle to recover the exact env
var names and DynamoDB key format (undocumented at the public-docs level), and writing three new CDK
constructs (`SpaInvalidation`, `SpaTagRevalidator`, `SpaTagCacheSeeder`) to reproduce, one piece at a
time, exactly what OpenNext's own reference architecture already does when deployed by its
officially supported target: **SST**. All three of ADR-0014's rejected/deferred pieces — the CDN
invalidation, the tag-cache seeding, and the seed Lambda's invocation contract — are things SST's
`sst.aws.Nextjs` component already solves natively.

ADR-0014 evaluated and rejected `cdklabs/cdk-nextjs` (correctly — it doesn't use `@opennextjs/aws` at
all) but never evaluated SST itself, beyond a single line in its own Mitigation Strategies section
noting the seed Lambda's contract could be "confirmed against ... the SST source, which wires both
today."

---

## Decision

Migrate **only the SPA's deploy layer** to SST v3 (Ion). Next.js/OpenNext itself is unchanged (ADR-0006
stands). The rest of the backend (Catalog, Basket, Ordering, Review, AppSync, User — all CDK) is
untouched; this is scoped entirely to how the SPA gets built and deployed to AWS.

### 1. SST v3 (Ion) does not use CloudFormation

SST v3 deploys via Pulumi (using the Terraform AWS provider internally) through `sst deploy`, with
its own state (S3-backed), not a CloudFormation stack. It coexists with the CDK-managed backend in
the same AWS account without conflict — the two are independent deployment mechanisms creating
ordinary AWS resources side by side. The one real implication: cross-stack references into
`DuckStoreAppSyncStack`'s CloudFormation exports (`ApiUrl`, `ApiKey`, `UserPoolClientId`,
`HostedUiUrl`), previously read via `cdk.Fn.importValue`, are now read directly with
`aws.cloudformation.getExportOutput({ name })` (a Pulumi AWS-provider data source) inside
`sst.config.ts`.

### 2. `sst.aws.Nextjs` replaces the hand-rolled constructs

One component (`src/WebApps/Shopping.Web.SPA.React/sst.config.ts`) replaces `spa-storage.ts`,
`spa-lambdas.ts`, `spa-distribution.ts`, `spa-invalidation.ts`, and `spa-tag-cache-seeder.ts`. It
creates and correctly wires the asset bucket, the CloudFront distribution, the server Lambda, and
the full ISR revalidation trio (`revalidationTable`, `revalidationQueue`, `revalidationFunction`) —
**including build-time tag-cache seeding**, via the same `dynamodb-provider` Lambda ADR-0014 deferred,
which SST already knows how to invoke correctly.

### 3. On-demand CDN invalidation is still a custom Lambda — now calling `revalidateTag()` via webhook, not writing DynamoDB directly

`sst.aws.Nextjs`'s `invalidation` config (`paths: "all" | "versioned" | string[]`) only fires at
**deploy time** — it has no concept of a business event driving cache staleness. The tag-driven
requirement (`CatalogUpdatedEvent`/`ReviewCreatedEvent` → mark tag stale → invalidate the affected
CloudFront path) is still a small custom Lambda, `revalidator/index.mjs`.

Its first iteration (ported near-verbatim from the deleted `infra/lambda/spa-tag-revalidator/index.mjs`)
wrote directly to the DynamoDB tag-cache table, mimicking what `revalidateTag()` does internally — a
schema/key-format reverse-engineered from a minified bundle (see Context). This was revised once the
inconsistency became clear: the SPA already had a **second, older** revalidation path
(`app/api/webhooks/review-created/route.ts`, called client-side after a review submission) that calls
Next.js's real `revalidateTag()` API instead. Having two different mechanisms for the same job — one
hand-rolled/undocumented, one using the public API — was itself a maintainability risk.

Rather than add a *third* route (`catalog-updated`) alongside it, both were replaced by a single
generic route, `app/api/webhooks/revalidate/route.ts`: callers just POST `{ tags: string[] }` — the
route has no idea whether that came from a catalog change, a review, or anything else added later. It
calls `revalidateTag()` for each tag. `revalidateTag()` alone still doesn't touch CloudFront, so
`revalidator/index.mjs` separately calls `CreateInvalidation` for the same paths, same as before.

This rewrite also fixed a real, silent bug and a real security weakness in the two routes it replaced:

- The old `review-created` route's *client-side* caller read
  `NEXT_PUBLIC_REVIEW_WEBHOOK_SECRET` — an env var that was never actually set anywhere in
  `sst.config.ts` or CI (only the unprefixed `REVIEW_WEBHOOK_SECRET` was, a server-only variable it
  couldn't see). The client-side call had silently never fired in any real deploy.
- Comparing a shared secret sent as a plain header (`x-webhook-secret`) is also weaker than it looks:
  the comparison itself wasn't timing-safe, and — more importantly for the browser-triggered call — a
  `NEXT_PUBLIC_*` secret is shipped straight into the JS bundle, so it was never actually confidential;
  anyone could read it from devtools and call the route directly to spam CloudFront invalidations
  (real money past the first 1,000/month).

The generic route is **server-to-server only** — there is deliberately no browser-triggered path at
all, not even a session-authenticated one. An earlier iteration had `review-form.tsx` call the webhook
client-side right after a review submission (for lower latency, since the browser knows about its own
mutation instantly, without waiting for the CDC pipeline), authenticated by the caller's session
(`resolveOwner()`) rather than a shared secret. That was removed: a client-triggered call only ever
covers reviews submitted through that one form, leaving the same kind of silent blind spot for reviews
created any other way (seed data, another channel) that this ADR's Context describes for Catalog. The
reviewer already sees their own review instantly via local React state
(`features/reviews/components/reviews-section.tsx`), independent of any server-side revalidation
timing, so the client-triggered call bought speed for other users at the cost of correctness — not a
trade worth making. `revalidateTag()` on this route is now reached exclusively via the event-driven
path, uniformly, for every tag.

Its one caller (the `revalidator` Lambda) authenticates with an HMAC-SHA256 signature over
`${timestamp}.${body}` (`x-webhook-signature`/`x-webhook-timestamp` headers) instead of a plain shared
secret sent as a header, verified with `crypto.timingSafeEqual`, timestamp checked against a 5-minute
window: the secret itself never goes over the wire, and a captured request/signature pair can't be
replayed indefinitely or reused for a different payload.

```ts
sst.aws.Bus.subscribe("Revalidator", eventBusArn, {
  handler: "revalidator/index.handler",
  environment: { SPA_URL, WEBHOOK_SECRET, CLOUDFRONT_DISTRIBUTION_ID },
  permissions: [
    { actions: ["cloudfront:CreateInvalidation"], resources: [distribution.arn] },
  ],
}, {
  pattern: { source: ["duckstore"], detailType: ["CatalogUpdatedEvent", "ReviewCreatedEvent"] },
});
```

`duckstore-event-bus` stays on CDK (created by Catalog) — the subscription targets it by ARN, the
same fixed-name-lookup relationship `spa-tag-revalidator.ts` (CDK) used via
`events.EventBus.fromEventBusName`, just expressed as an ARN since `Bus.subscribe` takes one.

This also fixes a client-side gap unrelated to the server/CDN layers: the browser's own Router Cache
(App Router client-side navigation cache, in-memory, `staleTimes` config) has no channel for a
server-side `revalidateTag()` call to reach it — there's no "push" from server to an already-open
tab. That's expected, not a bug: `router.refresh()`/`staleTimes` are the only two levers for that
layer, and are a separate concern from this ADR's scope (server + CDN consistency).

### 4. Two SST defaults had to be overridden

- **OpenNext version.** SST defaults to running its own pinned OpenNext version (`3.9.14` at the
  time of writing) via `npx open-next@<version> build`, ignoring this repo's intentionally-pinned
  `@opennextjs/aws@^4.0.3` devDependency and its `open-next.config.ts` overrides (a `sharp` binary
  cross-build fix for the image optimizer, needed for cross-compiling from a Mac). Fixed by setting
  `buildCommand: "pnpm build:opennext"` explicitly, so SST runs our own script/version instead of its
  bundled default.
- **`OPEN_NEXT_BUILD_ID`.** The installed SST version does not set this env var on the server
  function — the exact env var whose absence caused the original bug in the CDK version. Mitigated
  defensively with a `transform.server` hook in `sst.config.ts` that reads `.open-next/assets/BUILD_ID`
  (guaranteed to exist by the time the hook runs, since `buildCommand` has already executed as part
  of the component's own construction) and injects it into the server function's environment. **Not
  yet validated against a real deploy** — the verification step is to confirm the tag-revalidator's
  `Marked N entries stale` log shows `N > 0` on the first real `CatalogUpdatedEvent` after deploy,
  not the old `N = 0` bug recurring.

### 5. CI/CD

`.github/workflows/deploy-spa-cdk.yml` is replaced by `.github/workflows/deploy-spa-sst.yml`, running
`npx sst deploy --stage dev` instead of `cdk deploy DuckStoreSpaStack`. The AppSync
CloudFormation-output-fetch step now exports to `$GITHUB_ENV` (not `$GITHUB_OUTPUT`), since those
values must be inherited by the `pnpm build:opennext` child process `sst deploy` spawns internally
during the build — not just wired into the Lambda's runtime environment afterwards. The
old `REVIEW_WEBHOOK_SECRET` CDK `CfnParameter` is replaced by a single `WEBHOOK_SECRET` GitHub secret
/ `WebhookSecret` `sst.Secret`, set via `npx sst secret set WebhookSecret ...` — shared by the generic
`app/api/webhooks/revalidate/route.ts` and `revalidator/index.mjs`'s HMAC-signed calls to it.

### 6. Cutover: hard cutover, no parallel run

The old CDK stack (`DuckStoreSpaStack`) and its constructs were deleted in the same change that
introduced the SST app — no side-by-side/parallel-domain validation period. This is a single `dev`
environment with no production traffic at stake, so the extra safety of a staged cutover wasn't
judged worth the added complexity of running two deploy pipelines and two domains temporarily.

---

## Applies To

- `src/WebApps/Shopping.Web.SPA.React` — new `sst.config.ts`, new `revalidator/index.mjs`, new
  `app/api/webhooks/revalidate/route.ts` (replaces `catalog-updated`/`review-created`),
  `package.json` (`sst` devDependency, `@aws-sdk/client-cloudfront`).
- `infra` — removed `stacks/spa-stack.ts`, `constructs/spa-{storage,lambdas,distribution,invalidation,
  tag-revalidator,tag-cache-seeder}.ts`, `lambda/spa-tag-revalidator/`; `bin/app.ts` no longer
  instantiates a SPA stack (still computes the shared SPA domain for `AppSyncStack`'s Cognito
  callback allowlist).
- `.github/workflows/deploy-spa-cdk.yml` → `.github/workflows/deploy-spa-sst.yml`.
- Not affected: any backend `src/Services/*`, `infra/stacks/{catalog,basket,ordering,review,user,
  appsync}-stack.ts`.

---

## Consequences

### Positive
- SST's `Nextjs` component handles ISR tag-cache table creation, wiring, **and seeding** correctly
  out of the box — eliminating the exact class of bug (empty tag-cache table, unprefixed keys,
  un-invalidated CDN edge cache) that took a full debugging session to diagnose and hand-patch in the
  CDK version.
- Far less hand-rolled infrastructure surface area to keep in sync against future
  `@opennextjs/aws` upgrades — `spa-storage.ts`/`spa-lambdas.ts`/`spa-distribution.ts`/
  `spa-invalidation.ts`/`spa-tag-cache-seeder.ts` (five files, hundreds of lines, several of them
  reverse-engineered from a minified bundle) collapse into one `sst.config.ts`.
- The tag-driven CDN invalidation requirement is preserved exactly — no regression in
  revalidation behavior, just a smaller, more reliable implementation underneath it.

### Negative / Costs
- A second infrastructure-as-code engine now exists in the repo: Pulumi/Terraform-based (SST) for
  the SPA, CloudFormation-based (CDK) for everything else. Different mental model, different CLI,
  different state backend — a contributor touching both needs to hold both.
- SST's own OpenNext version defaults silently diverge from this repo's pinned version and
  `open-next.config.ts` overrides unless explicitly overridden (`buildCommand`) — an easy trap to
  reintroduce on a future SST upgrade if this ADR's reasoning isn't rechecked.
- The `OPEN_NEXT_BUILD_ID` gap was mitigated defensively but has not been validated against a real
  deploy as of this ADR — there is a real chance the fix is incomplete or unnecessary in ways that
  can only be confirmed by actually deploying and checking the revalidator's logs.

### Mitigation Strategies
- Re-derive `buildCommand`/`transform.server` reasoning whenever `sst` or `@opennextjs/aws` is
  upgraded — don't assume either still needs (or still lacks) today's workarounds.
- On the first real deploy, explicitly verify: (1) the tag-revalidator Lambda logs `Marked N entries
  stale` with `N > 0` after a real `CatalogUpdatedEvent`; (2) `curl -I` on the affected product path
  shows `x-cache: Miss from cloudfront` immediately after, with updated content.
- If a second stage/environment is added later, prefer validating SST's Nextjs component there
  before promoting further, even though this migration itself used a hard cutover for `dev`.

---

## Related Documentation

- [ADR-0006: React/Next.js SPA over Angular](./0006-react-nextjs-spa-over-angular.md)
- [ADR-0014: Deploy the React SPA to AWS via OpenNext, Hand-Rolled CDK](./0014-deploy-spa-via-opennext-hand-rolled-cdk.md) — superseded by this ADR
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
