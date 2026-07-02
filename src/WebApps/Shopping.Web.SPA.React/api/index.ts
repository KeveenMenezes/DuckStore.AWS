import { createGqlClient } from './graphql-client'
import { resolveEndpoint } from './endpoint-resolver'
import { getAuthHeaders, getPublicAuthHeaders } from './auth-provider'

/**
 * Pre-configured GraphQL client for use across the application.
 * Endpoint and auth are resolved once at module initialisation.
 */
export const gql = createGqlClient({
  endpoint: resolveEndpoint(),
  getAuthHeaders,
})

/**
 * Client for PUBLIC read queries (catalog, reviews). Same endpoint, but its
 * auth resolver never reads cookies — so Server Components that only fetch
 * public data (home, product detail) stay statically renderable (ISR) and
 * CDN-cacheable instead of being forced dynamic by a cookie access. Use `gql`
 * (cookie-aware) for anything that depends on the signed-in user.
 */
export const gqlPublic = createGqlClient({
  endpoint: resolveEndpoint(),
  getAuthHeaders: getPublicAuthHeaders,
})

export { GraphQLRequestError } from './graphql-client'
