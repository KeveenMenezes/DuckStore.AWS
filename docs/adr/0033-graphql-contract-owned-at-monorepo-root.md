# ADR-0033: GraphQL Contract and AppSync Resolvers Owned at the Monorepo Root, Not Inside the SPA

## Status
**Proposed** — July 2026

---

## Context

`graphql/schema.graphql` and `graphql/resolvers/**` lived inside
`src/WebApps/Shopping.Web.SPA.React/` — a historical accident: the React SPA was the first
GraphQL client, and its local graphql-yoga backend needed to read the schema. But these files are
not SPA code; they are the **API contract and the production resolver logic of AppSync**, shared
infrastructure with three consumers:

- **The CDK** (`infra/constructs/appsync-api.ts`) read the schema via `Definition.fromFile` and
  inlined every resolver `.js` from paths pointing *into a client application's directory* —
  infrastructure depending on frontend files.
- **The SPA's local yoga backend** (`app/api/graphql/local.ts`) reads the schema to simulate
  AppSync in dev.
- **The Blazor management app** programs against the same contract (mirrored by hand in
  `ProductAdminService.cs` / `Models.cs`) — a second client with no relationship to the SPA's
  directory.

The decisive aggravator: the SPA is slated to move into its own Git repository as a submodule
(ADR-0006 / CLAUDE.md). After that split, deploying the AppSync stack would require checking out a
frontend repo to obtain the API's own schema.

---

## Decision

**The GraphQL contract moves to `graphql/` at the monorepo root: `graphql/schema.graphql` and
`graphql/resolvers/<domain>/<queries|mutations>/<Type>.<field>.js`. It is owned by the platform,
not by any client.**

### 1. What moved and what stayed

| Artifact | Location | Owner |
|---|---|---|
| `schema.graphql` | `graphql/` (repo root) | Platform / AppSync |
| `resolvers/**` (APPSYNC_JS) | `graphql/resolvers/` (repo root) | Platform / AppSync |
| `types.ts` (TS mirror) | `src/WebApps/Shopping.Web.SPA.React/graphql/` | React SPA |

`types.ts` stays in the SPA deliberately: it is a *client-side mirror* of the contract, exactly as
`Models.cs` is the Blazor app's C# mirror. Each client owns its own typed view; the contract has
one neutral home.

### 2. Path updates (all mechanical)

- `infra/constructs/appsync-api.ts`: `SCHEMA_PATH`/`RESOLVERS_DIR` now resolve to the repo-root
  `graphql/`.
- `app/api/graphql/local.ts`: reads the schema from `../../../graphql/schema.graphql` relative to
  the SPA working directory.
- `.github/workflows/deploy-appsync-cdk.yml`: path trigger `graphql/**` replaces the SPA-scoped
  path.

### 3. Rules going forward

- Schema and resolver changes MUST happen under the root `graphql/` — new resolver files keep the
  `<domain>/<queries|mutations>/<Type>.<field>.js` convention (ADR-0007/0009).
- Client mirrors (`types.ts`, Blazor's `Models.cs`/operation strings) MUST be kept in sync with
  the root schema, as before — only the source-of-truth path changed.

---

## Applies To

- `graphql/**` (new root location)
- `infra/constructs/appsync-api.ts`
- `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`
- `.github/workflows/deploy-appsync-cdk.yml`

---

## Consequences

### Positive

- Infrastructure no longer depends on files inside a client app; the AppSync deploy is
  self-contained in the monorepo even after the SPA becomes a submodule.
- The contract's ownership matches reality: two clients (React SPA, Blazor admin) and two
  executors (AppSync, local yoga) all point at one neutral location.
- Deploy workflow triggers on `graphql/**` — schema changes no longer masquerade as SPA changes.

### Negative / Costs

- The SPA's local yoga backend now reaches outside its own directory (`../../../graphql/...`);
  when the SPA moves to its own repo, its dev backend will need the schema delivered another way
  (submodule layout keeps the relative path working from the monorepo checkout; a standalone
  clone of the SPA repo alone will not run yoga).
- The resolver/yoga duplication (each resolver exists as APPSYNC_JS and as a TypeScript
  simulation) is unchanged — this ADR moves the files, it does not unify the implementations.

### Mitigation Strategies

- When the SPA submodule split happens, revisit how `local.ts` obtains the schema (relative path
  from the monorepo checkout is expected to keep working; document it in the split's own ADR).

---

## References

- [ADR-0007: AppSync (GraphQL) as Client-Facing API](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0023: Decommission the YARP Gateway and Complete the Angular SPA Removal](./0023-decommission-yarp-gateway-and-angular-spa.md)
- [ADR-0032: Product Creation With Price as a Step Functions Express Saga Behind AppSync](./0032-create-product-with-price-step-functions-express-saga.md)
