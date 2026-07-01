import { getAuthHeaders } from '@/api/auth-provider'

/**
 * AppSync GraphQL proxy: resolves the same auth header the server-side `gql`
 * client uses (Bearer from the httpOnly access_token cookie, or the API Key
 * fallback for unauthenticated/public queries) and forwards the request to
 * AppSync.
 *
 * The browser never knows the AppSync URL — it always talks to /api/graphql.
 */
export async function handleAppSync(req: Request): Promise<Response> {
  const authHeaders = await getAuthHeaders()

  const res = await fetch(process.env.APPSYNC_URL!, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders,
    },
    body: await req.text(),
  })

  return new Response(res.body, {
    status: res.status,
    headers: { 'Content-Type': 'application/json' },
  })
}
