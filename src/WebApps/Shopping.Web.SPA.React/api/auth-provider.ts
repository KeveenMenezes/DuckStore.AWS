/**
 * Resolves the Cognito Access Token for the current request context.
 *
 * In DEV (no APPSYNC_URL) the local GraphQL handler bypasses AppSync entirely,
 * so auth is irrelevant and this returns undefined.
 *
 * In PROD, the Access Token is read from the httpOnly cookie set by
 * /api/auth/callback (PKCE flow). The graphql-client is called only from
 * Server Components and Route Handlers in the authenticated code paths;
 * client components always go through the /api/graphql BFF which reads the
 * cookie itself, so client-side callers never need this function.
 */
export async function getAuthToken(): Promise<string | undefined> {
  if (typeof window !== 'undefined') {
    // Client-side: token is in httpOnly cookie — not accessible from JS.
    // Authenticated client calls go through the /api/graphql BFF which adds the header.
    return undefined
  }

  try {
    // Dynamic import keeps `next/headers` out of the static import graph so
    // bundlers don't reject it when this module is pulled into a Client Component.
    const { cookies } = await import('next/headers')
    const cookieStore = await cookies()
    const accessToken = cookieStore.get('access_token')?.value
    return accessToken ? `Bearer ${accessToken}` : undefined
  } catch {
    // cookies() throws outside of a request context (e.g. during static generation)
    return undefined
  }
}
