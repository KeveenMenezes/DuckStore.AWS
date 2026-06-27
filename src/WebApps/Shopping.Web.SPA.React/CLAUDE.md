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
  products/      components, data, hooks, services, types
  theme/         context, hooks, types
shared/
  constants/     routes.ts, storage-keys.ts
  layout/        header, footer, hero-section, providers, user-dropdown
  lib/           format.ts, graphql-client.ts, id.ts, storage.ts
components/ui/   shadcn/ui primitives — do not modify these manually
app/             Next.js routes (page.tsx per route)
graphql/         schema + AppSync JS resolvers (prod) and shared types
```

Path alias `@/*` maps to the project root.

### GraphQL layer (two environments)

**Development** — `app/api/graphql/route.ts` is a Next.js Route Handler running `graphql-yoga`. It talks directly to DynamoDB Local via AWS SDK v3 and calls Basket Lambda functions via Aspire service-discovery env vars (`services__<name>__http__0`). Set `AWS_ENDPOINT_URL_DYNAMODB` (injected by Aspire's `WithReference(dynamoDb)`) and the Lambda service env vars to connect the dev environment.

**Production** — set `NEXT_PUBLIC_GRAPHQL_URL` to the AWS AppSync endpoint. The files under `graphql/resolvers/*.js` are **AppSync JS resolver** units (request/response functions using `@aws-appsync/utils`) — they are not run locally and are deployed to AppSync separately.

`graphql/types.ts` holds TypeScript interfaces that mirror `graphql/schema.graphql` — keep them in sync when the schema changes.

The thin fetch wrapper lives at `shared/lib/graphql-client.ts` (`gql<TData>(query, variables?)`).

### Lambda response casing

The .NET Lambda functions use `DefaultLambdaJsonSerializer`, which emits **PascalCase** JSON. The GraphQL route handler normalizes this to camelCase for the schema. When adding new Lambda-backed resolvers, remember to map `Item.PropertyName → item.propertyName`.

### State management

All state is React Context — no external store:

- `ThemeProvider` → `AuthProvider` → `CartProvider` → `ScoreProvider` (nesting order in `shared/layout/providers.tsx`)
- **Auth**: localStorage-only simulation (`features/auth/services/auth.service.ts`). No backend auth integration yet.
- **Cart**: in-memory React state. Synced to the Basket Lambda only at checkout time: `checkout.service.ts` calls `storeBasket` before `checkoutBasket`.
- **Score**: local score for the code challenges feature.

Each feature exposes a custom hook (`use-auth.ts`, `use-cart.ts`, etc.) that wraps `useContext` — always use the hook, never `useContext` directly.

### Product catalog

Driven by GraphQL: `products.service.ts` calls `getProducts()` and `getRawCategories()` which hit the `/api/graphql` local stub (dev) or the AppSync endpoint (prod). The stub reads DynamoDB Local directly. `features/products/data/products.data.ts` is now dead code (retained for reference; no longer imported).

### Pages

| Route | File | Notes |
|---|---|---|
| `/` | `app/page.tsx` | ISR (`revalidate=300`), renders product catalog |
| `/checkout` | `app/checkout/page.tsx` | Client-side checkout flow |
| `/challenges` | `app/challenges/page.tsx` | Interactive code quiz feature |
| `/my-profile` | `app/my-profile/page.tsx` | Auth-gated profile view |
| `/my-orders` | `app/my-orders/page.tsx` | Auth-gated order history |
