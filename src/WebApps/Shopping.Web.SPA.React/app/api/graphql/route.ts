// GRAPHQL_BACKEND selects the backend explicitly per environment:
// 'local' (dev) runs GraphQL Yoga against DynamoDB Local + Lambda emulator;
// 'appsync' (dev/staging/prod on AWS) proxies to AppSync with the Cognito
// Access Token from the httpOnly cookie.
//
// The two backends are dynamically imported (never both statically bundled) because
// local.ts reads the monorepo-root graphql/schema.graphql off disk at module load time
// via a path relative to process.cwd() — that path only exists in the local dev tree.
// A static `import { handleLocal } from './local'` pulls that module into every bundle,
// including the deployed appsync one, where the file doesn't exist: the read throws at
// import time and takes down every /api/graphql request with a 500, appsync branch or not.
const isLocal = process.env.GRAPHQL_BACKEND === 'local'

async function handle(request: Request): Promise<Response> {
  if (isLocal) {
    const { handleLocal } = await import('./local')
    return handleLocal(request)
  }
  const { handleAppSync } = await import('./appsync')
  return handleAppSync(request)
}

export const GET = handle
export const POST = handle
// CORS preflight for cross-origin local callers (the Blazor management app on
// localhost:7300 shares this dev backend) — graphql-yoga answers it with
// permissive CORS headers. In prod the Blazor app calls AppSync directly and
// the BFF proxy stays same-origin, so preflights there are just rejected.
export const OPTIONS = isLocal
  ? handle
  : () => new Response(null, { status: 405 })
