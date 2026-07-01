import { handleLocal } from './local'
import { handleAppSync } from './appsync'

// GRAPHQL_BACKEND selects the backend explicitly per environment:
// 'local' (dev) runs GraphQL Yoga against DynamoDB Local + Lambda emulator;
// 'appsync' (dev/staging/prod on AWS) proxies to AppSync with the Cognito
// Access Token from the httpOnly cookie.
const handle = process.env.GRAPHQL_BACKEND === 'local' ? handleLocal : handleAppSync

export const GET = handle
export const POST = handle
