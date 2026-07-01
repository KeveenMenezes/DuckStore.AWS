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
 * Access Token is read from the httpOnly cookie set by /api/auth/callback
 * (PKCE flow); requests without a session fall back to the API key so public
 * catalog queries keep working. Client components never need this: they
 * always go through the /api/graphql BFF (app/api/graphql/appsync.ts), which
 * resolves this same header server-side.
 */
export async function getAuthHeaders(): Promise<Record<string, string> | undefined> {
  if (typeof window !== 'undefined') return undefined
  if (!process.env.APPSYNC_URL) return undefined

  const apiKeyFallback = { 'x-api-key': process.env.APPSYNC_API_KEY! }

  try {
    // Dynamic import keeps `next/headers` out of the static import graph so
    // bundlers don't reject it when this module is pulled into a Client Component.
    const { cookies } = await import('next/headers')
    const cookieStore = await cookies()
    const accessToken = cookieStore.get('access_token')?.value
    return accessToken ? { Authorization: `Bearer ${accessToken}` } : apiKeyFallback
  } catch {
    // cookies() throws outside of a request context (e.g. during `next build`'s
    // static generation pass) — fall back to the API key so public catalog
    // queries still succeed at build time.
    return apiKeyFallback
  }
}
