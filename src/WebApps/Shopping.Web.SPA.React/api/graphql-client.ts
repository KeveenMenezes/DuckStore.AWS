interface GqlError {
  message: string
  locations?: Array<{ line: number; column: number }>
  path?: Array<string | number>
  extensions?: Record<string, unknown>
}

interface GqlResponse<T> {
  data?: T | null
  errors?: GqlError[]
}

export class GraphQLRequestError extends Error {
  constructor(
    message: string,
    public readonly errors: GqlError[],
  ) {
    super(message)
    this.name = 'GraphQLRequestError'
  }
}

export interface GqlClientConfig {
  endpoint: string
  getToken?: () => Promise<string | undefined>
}

/**
 * Creates a GraphQL client bound to the given endpoint and optional token provider.
 * Pure fetch wrapper — no environment detection, no auth logic, no side effects.
 */
export function createGqlClient(config: GqlClientConfig) {
  return async function gql<TData, TVariables = Record<string, unknown>>(
    query: string,
    variables?: TVariables,
    init?: RequestInit,
  ): Promise<TData> {
    const token = await config.getToken?.()
    const { headers: extraHeaders, ...restInit } = init ?? {}

    const res = await fetch(config.endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: token } : {}),
        ...extraHeaders,
      },
      body: JSON.stringify({ query, variables }),
      ...restInit,
    })

    if (!res.ok) {
      throw new Error(`GraphQL HTTP error: ${res.status} ${res.statusText}`)
    }

    const json = (await res.json()) as GqlResponse<TData>

    if (json.errors?.length) {
      throw new GraphQLRequestError(
        json.errors.map(e => e.message).join('\n'),
        json.errors,
      )
    }

    if (json.data == null) {
      throw new Error('GraphQL response contained no data and no errors')
    }

    return json.data
  }
}
