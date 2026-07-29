/**
 * Resolves the AppSync auth header for the current request context.
 *
 * In DEV (no APPSYNC_URL) the local GraphQL handler bypasses AppSync entirely,
 * so auth is irrelevant and this returns undefined.
 *
 * In PROD, server-side callers (Server Components, Route Handlers, and the
 * shared `gql` client resolved in api/index.ts) talk to AppSync directly —
 * looping back through this app's own /api/graphql would fail during `next
 * build`'s static generation pass, since no server is listening yet. The
 * ID Token comes from the server-side session (ADR-0041) — it carries the
 * identity claims (email, name, sub, cognito:groups) the AppSync resolvers rely
 * on (the access token has no email/name), and getSession() guarantees it is
 * unexpired, refreshing it transparently when needed. Requests without a session
 * fall back to the API key so public catalog queries keep working. Client
 * components never need this: they always go through the /api/graphql BFF
 * (app/api/graphql/appsync.ts), which resolves this same header server-side.
 *
 * Errors deliberately propagate. Falling back to the API key on failure would answer a
 * user-scoped query with guest authority — the caller gets someone else's empty basket or order
 * list and renders it as fact, which is indistinguishable from being signed out. Only build-time
 * "there is no request" resolves to no session, and getSession() already handles that case, so
 * anything reaching here is a real outage and is better surfaced than papered over. Public reads
 * never take this path: they use getPublicAuthHeaders() below.
 */
export async function getAuthHeaders(): Promise<Record<string, string> | undefined> {
  if (typeof window !== 'undefined') return undefined
  if (!process.env.APPSYNC_URL) return undefined

  // Dynamic import keeps `next/headers` and the DynamoDB client out of the static import graph
  // so bundlers don't reject this module when it's pulled into a Client Component's graph
  // (api/index.ts is imported from both sides).
  const { getSession } = await import('@/lib/auth/session')
  const session = await getSession()

  return session ? { Authorization: `Bearer ${session.idToken}` } : { 'x-api-key': process.env.APPSYNC_API_KEY! }
}

/**
 * Auth headers for PUBLIC read queries (catalog, reviews). Deliberately never
 * touches the session — resolving one reads a cookie, and reading a cookie during
 * render forces the route to be dynamic, which is exactly what stopped `/` and
 * `/products/[id]` from being statically generated / CDN-cached. These queries
 * don't need a user token (the API key authorizes them), so server-side we return
 * the key directly and stay cookie-free; the browser still goes through the
 * /api/graphql BFF.
 */
export async function getPublicAuthHeaders(): Promise<Record<string, string> | undefined> {
  if (typeof window !== 'undefined') return undefined
  if (!process.env.APPSYNC_URL) return undefined
  return { 'x-api-key': process.env.APPSYNC_API_KEY! }
}
