---
name: rendering-strategy
description: Use when deciding between SSR, SSG, or ISR for a Next.js page in the React SPA — e.g. "which strategy should this page use?", "should this be SSR or SSG?", "how do I set up ISR here?". Applies the project's rendering rules: SSR for dynamic/personalized content, SSG for truly static content, ISR for static content invalidated by events (never by TTL). Hydration is not a strategy.
---

# Rendering strategy skill

Recommends and implements the correct rendering strategy for a Next.js App Router page in
`src/WebApps/Shopping.Web.SPA.React`. The three strategies and their criteria:

| Strategy | Use when |
|---|---|
| **SSR** | Content varies per request or per user (personalized, auth-gated, real-time) |
| **SSG** | Content is static and changes only with a new deployment |
| **ISR** | Content is static but must update when a backend event fires (not on a timer) |

**Hydration is not a rendering strategy.** It is the process by which React activates
client-side interactivity on top of already-rendered HTML. Adding `"use client"` to a
component or using client-side state does not change the page's rendering strategy — those
concerns are orthogonal.

## Decision flow

Work through these questions in order and stop at the first match:

1. **Is the content personalized or session-dependent?** (current user's cart, order history,
   profile) → **SSR**. The content differs per request so it cannot be pre-rendered.

2. **Does rendering require data that only exists at request time?** (cookies, auth headers,
   geo-IP, real-time prices) → **SSR**. Accessing `cookies()`, `headers()`, or any other
   Dynamic API in Next.js forces SSR automatically — don't fight it with hacks.

3. **Does the content need to update in response to a backend event, but can serve stale
   HTML between events?** (product catalog refreshed after a DynamoDB write, challenge list
   updated after content publish) → **ISR**. Invalidation is event-driven, not time-driven.

4. **Is the content fully static and only changes with a deployment?** (marketing copy,
   static challenge data loaded from a data file) → **SSG**.

When in doubt between ISR and SSG, ask: "does a content editor or a service need to
push updates without a redeploy?" If yes, ISR. If no, SSG.

## Cache invalidation rule (mandatory)

**Cache invalidation is exclusively event-driven.** Never use a short `revalidate` number
as a workaround for keeping content fresh. Using `revalidate = 60` or similar turns ISR
into polling — it adds server load and doesn't reflect events reliably.

Correct ISR pattern: `revalidate = false` on the page + call `revalidateTag()` or
`revalidatePath()` inside the event handler Route that receives the backend event (e.g. a
DynamoDB Stream → Lambda → webhook to the Next.js app).

## Next.js App Router patterns

### SSR
```tsx
// No revalidate export — Next.js defaults to dynamic when Dynamic APIs are used.
// Or force it explicitly:
export const dynamic = 'force-dynamic'

export default async function Page() {
  const session = await getSession() // reads cookies → forces SSR automatically
  const data = await fetchPersonalizedData(session.userId)
  return <View data={data} />
}
```

### SSG
```tsx
// Static by default when no Dynamic APIs are used and no revalidate is set.
// Explicit opt-in:
export const revalidate = false

export default async function Page() {
  const data = await fetchStaticData() // fetched at build time only
  return <View data={data} />
}
```

### ISR (event-driven invalidation)
```tsx
// page.tsx — mark as static; never use a short TTL number here.
export const revalidate = false

// Tag the fetch so the event handler can invalidate it by tag.
export default async function Page() {
  const data = await fetch('/api/graphql', {
    method: 'POST',
    next: { tags: ['products'] },   // ← tag used for targeted invalidation
    body: JSON.stringify({ query: PRODUCTS_QUERY }),
  })
  return <View data={await data.json()} />
}
```

```tsx
// app/api/webhooks/catalog-updated/route.ts
// Called by the Lambda (or EventBridge → Lambda → HTTP) when DynamoDB changes.
import { revalidateTag } from 'next/cache'
import { NextResponse } from 'next/server'

export async function POST(req: Request) {
  // Validate the event source here before trusting the payload.
  revalidateTag('products')
  return NextResponse.json({ revalidated: true })
}
```

Do **not** use `revalidatePath` as the default — it invalidates the full route cache.
Prefer `revalidateTag` with a scoped tag so only the relevant data is dropped.

## What to read before recommending

1. Read the target `page.tsx` to identify:
   - Whether it calls `cookies()`, `headers()`, or other Dynamic APIs.
   - Whether it imports from `features/*/data/*.data.ts` (static file → SSG candidate) or
     calls `gql()` / fetch at runtime (dynamic source → ISR or SSR candidate).
   - The existing `export const revalidate` or `export const dynamic` directives, if any.
2. Check `app/api/graphql/route.ts` if the page fetches via GraphQL — the resolver tells you
   whether the data comes from DynamoDB directly (ISR viable) or from a Lambda that reads
   session state (SSR required).

## After recommending

- Add or update the `export const revalidate` / `export const dynamic` directive in the
  page file.
- If ISR: confirm there is (or describe what is needed for) a webhook Route Handler that
  calls `revalidateTag`. If no event source exists yet, call it out — ISR without an
  invalidation trigger is just SSG with extra ceremony.
- If SSR on a page that currently has `"use client"` components: clarify that the
  client components still hydrate normally; the rendering strategy applies to the Server
  Component shell, not the client subtree.
- Never suggest `revalidate = <number>` as the ISR implementation — the project rule
  prohibits TTL-based invalidation.
