import { getAuthHeaders } from '@/api/auth-provider'
import { prepareBasketRequest, guestCookieHeader } from '@/lib/basket-bff'

/**
 * AppSync GraphQL proxy: resolves the same auth header the server-side `gql`
 * client uses (Bearer from the httpOnly access_token cookie, or the API Key
 * fallback for guests/public queries) and forwards the request to AppSync.
 *
 * Before forwarding, it injects the BFF-resolved ownerId into basket operations
 * (see prepareBasketRequest) so the browser never sends an identity, and sets the
 * httpOnly guest cookie for new/renewed visitor carts.
 *
 * The browser never knows the AppSync URL — it always talks to /api/graphql.
 */
export async function handleAppSync(req: Request): Promise<Response> {
  const authHeaders = await getAuthHeaders()
  const { body, owner, setGuestCookie } = await prepareBasketRequest(await req.text())

  const res = await fetch(process.env.APPSYNC_URL!, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders,
    },
    body,
  })

  const headers = new Headers({ 'Content-Type': 'application/json' })
  if (setGuestCookie) headers.append('Set-Cookie', guestCookieHeader(owner))

  return new Response(res.body, { status: res.status, headers })
}
