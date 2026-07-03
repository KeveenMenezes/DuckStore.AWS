import { resolveOwner, GUEST_COOKIE, GUEST_MAX_AGE_SECONDS, type Owner } from './identity'

// Injects the BFF-resolved ownerId into the GraphQL variables of the basket operations that
// declare $ownerId (basket, storeBasket, deleteBasket). The browser never sends an ownerId —
// identity lives only in the httpOnly cookies resolveOwner() reads. checkoutBasket/mergeBasket
// are Cognito-only and derive the owner from ctx.identity in the AppSync resolver, so they carry
// no $ownerId and are left untouched here.

export type PreparedRequest = {
  body: string
  owner: Owner
  setGuestCookie: boolean
}

export async function prepareBasketRequest(rawBody: string): Promise<PreparedRequest> {
  const owner = await resolveOwner()

  let parsed: { query?: string; variables?: Record<string, unknown> } | null = null
  try {
    parsed = JSON.parse(rawBody)
  } catch {
    parsed = null
  }

  const query = parsed?.query ?? ''

  if (parsed && query.includes('$ownerId')) {
    parsed.variables = { ...(parsed.variables ?? {}), ownerId: owner.ownerId }
  }

  // Persist a freshly minted guest id, and slide the 15-day window on guest writes.
  const isMutation = /(^|[\s{])mutation\b/.test(query)
  const setGuestCookie = owner.type === 'guest' && (owner.isNewGuest || isMutation)

  return {
    body: parsed ? JSON.stringify(parsed) : rawBody,
    owner,
    setGuestCookie,
  }
}

// Serialized httpOnly guest cookie — the guest id is never exposed to JavaScript.
export function guestCookieHeader(owner: Owner): string {
  const secure = process.env.NODE_ENV === 'production' ? '; Secure' : ''
  return `${GUEST_COOKIE}=${owner.guestId}; Path=/; Max-Age=${GUEST_MAX_AGE_SECONDS}; HttpOnly; SameSite=Lax${secure}`
}
