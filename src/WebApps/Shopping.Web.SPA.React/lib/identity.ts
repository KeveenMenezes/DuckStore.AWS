import { cookies } from 'next/headers'

// Single source of truth for the caller's identity. Server-only: it reads the httpOnly
// cookies the browser can't see. Every basket path resolves the owner here instead of
// each route/service deciding "token vs guest" on its own.

export const GUEST_COOKIE = 'guest_id'
export const GUEST_MAX_AGE_SECONDS = 60 * 60 * 24 * 15 // 15 days

export type Owner = {
  ownerId: string // USER#<cognito-sub> (authenticated) or GUEST#<guestId> (visitor)
  type: 'user' | 'guest'
  guestId: string | null // raw guest uuid when a guest cookie exists or was just minted
  isNewGuest: boolean // a guest id was generated this request and must be persisted
}

// The access token is a Cognito-signed JWT set server-side during the PKCE callback; we only
// need its `sub` claim and decode the payload without re-verifying (same trust as /api/auth/me).
function subFromJwt(token: string): string | null {
  try {
    const payload = token.split('.')[1]
    if (!payload) return null
    const decoded = JSON.parse(Buffer.from(payload, 'base64url').toString('utf-8'))
    return (decoded.sub as string) ?? null
  } catch {
    return null
  }
}

export async function resolveOwner(): Promise<Owner> {
  const store = await cookies()
  const accessToken = store.get('access_token')?.value
  const guestId = store.get(GUEST_COOKIE)?.value ?? null

  if (accessToken) {
    const sub = subFromJwt(accessToken)
    if (sub) return { ownerId: `USER#${sub}`, type: 'user', guestId, isNewGuest: false }
  }

  if (guestId) return { ownerId: `GUEST#${guestId}`, type: 'guest', guestId, isNewGuest: false }

  const generated = crypto.randomUUID()
  return { ownerId: `GUEST#${generated}`, type: 'guest', guestId: generated, isNewGuest: true }
}
