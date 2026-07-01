import { createGqlClient } from './graphql-client'
import { resolveEndpoint } from './endpoint-resolver'
import { getAuthHeaders } from './auth-provider'

/**
 * Pre-configured GraphQL client for use across the application.
 * Endpoint and auth are resolved once at module initialisation.
 */
export const gql = createGqlClient({
  endpoint: resolveEndpoint(),
  getAuthHeaders,
})

export { GraphQLRequestError } from './graphql-client'
