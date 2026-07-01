# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## About this project

React/Next.js 16 SPA for DuckStore — a rubber duck e-commerce store with code challenges. This is the primary frontend (ADR-0006) and is slated to move into its own git repo linked back as a submodule. Uses the App Router, React 19, TypeScript (strict), Tailwind CSS v4, and shadcn/ui.

Package manager is **pnpm**. The full system (DynamoDB Local, Lambda emulator, etc.) is started from the Aspire AppHost one level up.

## Commands

```bash
pnpm dev          # Start Next.js dev server (http://localhost:3000)
pnpm build        # Production build
pnpm lint         # ESLint
```

TypeScript build errors are intentionally suppressed in `next.config.mjs` (`ignoreBuildErrors: true`) — use `tsc --noEmit` to check types separately.

## Architecture

### Feature-sliced design

Code is organized by feature slice, not by layer:

```
features/
  auth/          components, context, hooks, services, types
  cart/          components, context, hooks, types
  challenges/    components, context, data, hooks, services, types
  checkout/      components, hooks, services, types
  products/      components, hooks, services, types
  reviews/       components, services, types
  theme/         context, hooks, types
shared/
  constants/     routes.ts, storage-keys.ts
  layout/        header, footer, hero-section, providers, user-dropdown
  lib/           format.ts, id.ts, storage.ts
api/             GraphQL client used by client components and Server Components alike
  index.ts               pre-built `gql` instance (endpoint + auth resolved once)
  graphql-client.ts       pure fetch wrapper, `createGqlClient({ endpoint, getToken })`
  endpoint-resolver.ts    picks the GraphQL endpoint for the current runtime context
  auth-provider.ts        resolves the Cognito Access Token server-side (cookie)
  queries/, mutations/, fragments/   one file per operation/entity
components/ui/   shadcn/ui primitives — do not modify these manually
app/             Next.js routes (page.tsx per route) + Route Handlers under app/api/
graphql/         schema.graphql, AppSync JS resolvers (prod), and graphql/types.ts
```

Path alias `@/*` maps to the project root.

### GraphQL layer

The browser and Server Components never talk to AppSync or DynamoDB directly — they always call the `gql` client from `api/index.ts`, which POSTs to a single endpoint resolved by `api/endpoint-resolver.ts`:

- Client-side (browser): relative `/api/graphql`.
- Server-side (RSC/SSG/ISR): absolute `${NEXT_PUBLIC_SITE_URL}/api/graphql` (Node `fetch` needs an absolute URL).
- Escape hatch: if `NEXT_PUBLIC_APPSYNC_URL` is set, the browser calls AppSync directly instead — not used in the deployed config today (see `[[project-appsync-security]]`, which keeps AppSync env vars server-only).

`app/api/graphql/route.ts` is the Next.js Route Handler backing that endpoint. It branches on `GRAPHQL_BACKEND` between two implementations in the same folder:

- `local.ts` (`GRAPHQL_BACKEND=local`, dev) — runs `graphql-yoga` against DynamoDB Local via AWS SDK v3 (`AWS_ENDPOINT_URL_DYNAMODB`, injected by Aspire's `WithReference(dynamoDb)`) and invokes the Basket Lambda through the Aspire Lambda emulator (`AWS_ENDPOINT_URL_LAMBDA`). It strips the `@aws_api_key`/`@aws_cognito_user_pools` directives from `graphql/schema.graphql` before building the schema, since Yoga doesn't understand AppSync-only directives.
- `appsync.ts` (`GRAPHQL_BACKEND=appsync`, staging/prod) — a **BFF proxy**: reads the Cognito Access Token from the httpOnly `access_token` cookie (set by `/api/auth/callback`) and forwards the request to real AppSync as `Authorization: Bearer`, falling back to `x-api-key` for unauthenticated/public queries. The browser never sees the AppSync URL or API key.

Auth token resolution for **server-side** `gql` calls (Server Components, Route Handlers) goes through `api/auth-provider.ts`, which dynamic-imports `next/headers` to read the same `access_token` cookie — kept dynamic so bundlers don't choke when the module is pulled into a Client Component's import graph. Client-side callers never need this: they always go through the `/api/graphql` BFF, which adds the header itself.

`/api/auth/{login,callback,logout,me}` implement the Cognito Hosted UI PKCE flow. See `[[project-appsync-security]]` for the full Cognito/AppSync security architecture (auth-per-operation table, Groups, env vars).

The files under `graphql/resolvers/*.js` are **AppSync JS resolver** units (request/response functions using `@aws-appsync/utils`) — not run locally, deployed to AppSync separately. `graphql/types.ts` holds TypeScript interfaces that mirror `graphql/schema.graphql` — keep them in sync when the schema changes.

### Lambda response casing

The .NET Lambda functions use `DefaultLambdaJsonSerializer`, which emits **PascalCase** JSON. `app/api/graphql/local.ts` normalizes this to camelCase for the schema (e.g. the Basket item stored in the `Data` attribute is PascalCase JSON from the .NET serializer). When adding new Lambda-backed resolvers, remember to map `Item.PropertyName → item.propertyName`.

### State management

All state is React Context — no external store:

- `ThemeProvider` → `AuthProvider` → `CartProvider` → `ScoreProvider` (nesting order in `shared/layout/providers.tsx`)
- **Auth**: hybrid. `AuthProvider` first checks for a real Cognito session via `GET /api/auth/me`; if that returns nothing it falls back to the localStorage-only simulation (`features/auth/services/auth.service.ts`). `loginWithCognito()` kicks off the PKCE flow; `login`/`register` are the local-simulation path.
- **Cart**: React state, hydrated on mount from the Basket Lambda (`GET_BASKET` GraphQL query, keyed by a stable guest `userName`) and kept in sync continuously — every change is pushed back via a 300ms-debounced `syncCartToBasket` call (`features/cart/services/basket.service.ts`), not just at checkout. `features/checkout/services/checkout.service.ts` relies on this and no longer calls `storeBasket` itself before `checkoutBasket`.
- **Score**: local-only state for the code challenges feature, no persistence/sync.

Each feature exposes a custom hook (`use-auth.ts`, `use-cart.ts`, etc.) that wraps `useContext` — always use the hook, never `useContext` directly.

### Product catalog

Driven by GraphQL through the `api/` client: `features/products/services/products.service.ts` calls `getProducts()`/`getProduct()`/`getRawCategories()`, which resolve to `/api/graphql` (dev: Yoga + DynamoDB Local; prod: BFF proxy to AppSync). There is no static/mock product data file anymore — everything is fetched.

### Pages

Per the `rendering-strategy` skill: SSR for personalized data, SSG for content identical to every user, ISR (invalidated by webhook, never TTL) for static content that changes on writes elsewhere.

| Route | File | Strategy | Notes |
|---|---|---|---|
| `/` | `app/page.tsx` | ISR (`revalidate=false`) | Fetches products/categories tagged `products` (generic, home-wide). Invalidated by the `SpaRevalidationWebhook` Lambda (`infra/constructs/spa-revalidation-webhook.ts`), which reacts to `CatalogUpdatedEvent` off EventBridge directly — no HTTP webhook route for this anymore. `ProductCatalog` (`"use client"`) only does client-side category filtering on the props it receives — no fetch of its own. |
| `/products/[id]` | `app/products/[id]/page.tsx` | ISR (`revalidate=false`) | Tags `products:{id}` (product data/rating) and `reviews:{id}` (that product's reviews) — granular per-product, not the home page's generic `products` tag, so one product changing doesn't revalidate every other product's page. `reviews:{id}` is invalidated by `POST /api/webhooks/review-created` (still HTTP — triggered client-side by `review-form.tsx` right after a review submission); also invalidated server-side by `SpaRevalidationWebhook` reacting to `ReviewCreatedEvent`, for reviews created outside the SPA. `products:{id}` is invalidated by `SpaRevalidationWebhook` reacting to `CatalogUpdatedEvent`, which now carries the changed product's `ProductId` (from the DynamoDB Streams record key). |
| `/checkout` | `app/checkout/page.tsx` | SSG (`revalidate=false`) | Server shell only; `CheckoutView` (`"use client"`) hydrates cart/auth client-side. |
| `/challenges` | `app/challenges/page.tsx` | SSG (`revalidate=false`) | Challenge list is static data compiled into the bundle (`getChallenges()`, no runtime fetch); per-user score hydrates client-side via `ScoreProvider`. |
| `/my-profile` | `app/my-profile/page.tsx` | SSG (`revalidate=false`) | Server shell only; `ProfileView` (`"use client"`) hydrates auth/user data client-side. |
| `/my-orders` | `app/my-orders/page.tsx` | SSG (`revalidate=false`) | Server shell only; `OrdersView` (`"use client"`) hydrates auth/order data client-side. |
| `/cart` | `app/cart/page.tsx` | CSR | Whole page is `"use client"` — reads `CartProvider` state directly, no server fetch. |
