/** Thin GraphQL client. In dev, points at /api/graphql (local stub).
 *  In prod, set NEXT_PUBLIC_GRAPHQL_URL to the AppSync endpoint. */

function resolveGraphqlUrl(): string {
  // Explicit endpoint (e.g. AppSync in prod) is used everywhere when set.
  if (process.env.NEXT_PUBLIC_GRAPHQL_URL) return process.env.NEXT_PUBLIC_GRAPHQL_URL

  // Browser: a relative URL resolves against the current origin.
  if (globalThis.window !== undefined) return '/api/graphql'

  // Server (RSC/SSG/ISR/SSR): Node's fetch requires an absolute URL.
  const origin = process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3000'
  return `${origin}/api/graphql`
}

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

export async function gql<TData, TVariables = Record<string, unknown>>(
  query: string,
  variables?: TVariables,
  init?: RequestInit,
): Promise<TData> {
  const { headers: extraHeaders, ...restInit } = init ?? {}

  const res = await fetch(resolveGraphqlUrl(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...extraHeaders },
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
