import { handleLocal } from './local'
import { handleAppSync } from './appsync'

// GRAPHQL_BACKEND selects the backend explicitly per environment:
// 'local' (dev) runs GraphQL Yoga against DynamoDB Local + Lambda emulator;
// 'appsync' (dev/staging/prod on AWS) proxies to AppSync with the Cognito
// Access Token from the httpOnly cookie.
const handle = process.env.GRAPHQL_BACKEND === 'local' ? handleLocal : handleAppSync

export const GET = handle
export const POST = handle
// CORS preflight for cross-origin local callers (the Blazor management app on
// localhost:7300 shares this dev backend) — graphql-yoga answers it with
// permissive CORS headers. In prod the Blazor app calls AppSync directly and
// the BFF proxy stays same-origin, so preflights there are just rejected.
export const OPTIONS =
  process.env.GRAPHQL_BACKEND === 'local'
    ? handleLocal
    : () => new Response(null, { status: 405 })
