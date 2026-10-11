interface GqlError {
  message: string
  locations?: Array<{ line: number; column: number }>
  path?: Array<string | number>
  extensions?: Record<string, unknown>
  // AppSync puts util.error()'s type and info at the top level of the error; graphql-yoga (the
  // local backend) puts them under extensions.
  errorType?: string
  errorInfo?: Record<string, unknown> | null
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

  /** The first error's typed errorType (e.g. "PriceChanged"), whichever backend raised it. */
  get errorType(): string | undefined {
    const first = this.errors[0]
    return first?.errorType ?? (first?.extensions?.errorType as string | undefined)
  }

  /** The first error's errorInfo (e.g. { serverTotal }), whichever backend raised it. */
  get errorInfo(): Record<string, unknown> | undefined {
    const first = this.errors[0]
    return first?.errorInfo ?? (first?.extensions?.errorInfo as Record<string, unknown> | undefined) ?? undefined
  }
}

export interface GqlClientConfig {
  endpoint: string
  getAuthHeaders?: () => Promise<Record<string, string> | undefined>
}

/**
 * Creates a GraphQL client bound to the given endpoint and optional auth header provider.
 * Pure fetch wrapper — no environment detection, no auth logic, no side effects.
 */
export function createGqlClient(config: GqlClientConfig) {
  return async function gql<TData, TVariables = Record<string, unknown>>(
    query: string,
    variables?: TVariables,
    init?: RequestInit,
  ): Promise<TData> {
    const authHeaders = await config.getAuthHeaders?.()
    const { headers: extraHeaders, ...restInit } = init ?? {}

    const res = await fetch(config.endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders,
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
