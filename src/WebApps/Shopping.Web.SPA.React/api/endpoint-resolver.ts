/**
 * Resolves the GraphQL endpoint for the current runtime context.
 *
 * Browser            → /api/graphql (BFF proxy; never sees AppSync URL/API key)
 * Server, PROD       → APPSYNC_URL directly (avoids a self-referential HTTP
 *                       loopback that would fail during `next build`'s static
 *                       generation pass, since no server is listening yet)
 * Server, DEV        → /api/graphql (loops back into the in-process local
 *                       GraphQL Yoga handler; safe because `next dev` already
 *                       has a server running when this runs)
 */
export function resolveEndpoint(): string {
  if (process.env.NEXT_PUBLIC_APPSYNC_URL) return process.env.NEXT_PUBLIC_APPSYNC_URL

  if (globalThis.window !== undefined) return '/api/graphql'

  if (process.env.APPSYNC_URL) return process.env.APPSYNC_URL

  const origin = process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3000'
  return `${origin}/api/graphql`
}
