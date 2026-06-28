/**
 * Resolves the GraphQL endpoint for the current runtime context.
 *
 * DEV  → /api/graphql  (Next.js route handler → handleLocal → Lambda Emulator)
 * PROD → NEXT_PUBLIC_APPSYNC_URL  (browser calls AppSync directly)
 */
export function resolveEndpoint(): string {
  if (process.env.NEXT_PUBLIC_APPSYNC_URL) return process.env.NEXT_PUBLIC_APPSYNC_URL

  // Dev — browser: relative URL is sufficient.
  if (globalThis.window !== undefined) return '/api/graphql'

  // Dev — server (RSC/SSG/ISR/SSR): Node fetch requires an absolute URL.
  const origin = process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3000'
  return `${origin}/api/graphql`
}
