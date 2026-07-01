import { handleLocal } from './local'
import { handleProd } from './prod'

// In production, APPSYNC_URL is set (server-only env var) and all requests are
// proxied to AppSync with the Cognito Access Token from the httpOnly cookie.
// In development, GraphQL Yoga handles requests against DynamoDB Local + Lambda emulator.
const handle = process.env.APPSYNC_URL ? handleProd : handleLocal

export const GET = handle
export const POST = handle
