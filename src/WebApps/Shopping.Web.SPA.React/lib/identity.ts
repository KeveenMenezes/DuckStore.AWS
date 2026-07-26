import { cookies } from 'next/headers'
import { getSession } from './auth/session'

// Single source of truth for the caller's identity (ADR-0016 §2). Server-only: it reads the
// httpOnly guest cookie the browser can't see, and the session the browser can't see at all.
// Every basket path resolves the owner here instead of each route/service deciding
// "token vs guest" on its own.

export const GUEST_COOKIE = 'guest_id'
export const GUEST_MAX_AGE_SECONDS = 60 * 60 * 24 * 15 // 15 days

export type Owner = {
  ownerId: string // USER#<cognito-sub> (authenticated) or GUEST#<guestId> (visitor)
  type: 'user' | 'guest'
  guestId: string | null // raw guest uuid when a guest cookie exists or was just minted
  isNewGuest: boolean // a guest id was generated this request and must be persisted
}

export async function resolveOwner(): Promise<Owner> {
  const store = await cookies()
  const guestId = store.get(GUEST_COOKIE)?.value ?? null

  // `sub` comes from the session record, where it was written after the ID token's claims were
  // validated at sign-in (ADR-0041 §6). Nothing here decodes a JWT: this value scopes cart data in
  // DynamoDB, so it must not come from a token the request itself carried.
  const session = await getSession()
  if (session) {
    return { ownerId: `USER#${session.sub}`, type: 'user', guestId, isNewGuest: false }
  }

  if (guestId) return { ownerId: `GUEST#${guestId}`, type: 'guest', guestId, isNewGuest: false }

  const generated = crypto.randomUUID()
  return { ownerId: `GUEST#${generated}`, type: 'guest', guestId: generated, isNewGuest: true }
}
