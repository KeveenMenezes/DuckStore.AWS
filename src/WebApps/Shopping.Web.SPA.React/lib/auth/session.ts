import { cache } from 'react'
import { cookies } from 'next/headers'
import { randomBytes } from 'node:crypto'
import { refreshAccessToken, revokeRefreshToken } from '@/lib/cognito-refresh'
import {
  deleteSessionRecord,
  getSessionRecord,
  putSessionRecord,
  rotateSessionTokens,
  type SessionRecord,
} from './session-store'

/**
 * The single source of truth for authentication state (ADR-0041).
 *
 * Cognito's access/id tokens are 1h-lived; a session is 30 days. Conflating the two is what made
 * the app log users out after an hour — the browser's cookie jar was acting as the session state
 * machine. Here the browser holds only an opaque SessionId; tokens live server-side and are
 * refreshed transparently, so credential expiry is never observable as a change in auth state.
 *
 * No module outside lib/auth/ may read a Cognito token cookie — there are none. This is enforced
 * by the no-restricted-syntax rule in eslint.config.mjs, not by convention.
 */

// __Host- forces Secure + Path=/ and forbids a Domain attribute, which is what makes the cookie
// un-shadowable from a sibling subdomain (cookie tossing). It requires HTTPS, so dev uses a plain
// name — local dev has no Cognito to sign in against anyway (ADR-0017).
export const SESSION_COOKIE = process.env.NODE_ENV === 'production' ? '__Host-sid' : 'sid'

// Matches SpaClient.refreshTokenValidity in infra/constructs/appsync-auth.ts, and doubles as the
// absolute session cap: ExpiresAt is set once at creation and never slid by activity.
export const SESSION_MAX_AGE_SECONDS = 60 * 60 * 24 * 30

// Renew this far before expiry so a token never dies in flight at AppSync, and to absorb clock
// drift between this Lambda and Cognito.
const REFRESH_SKEW_SECONDS = 60

export type Session = {
  sessionId: string
  sub: string
  email: string
  name?: string
  /** Always unexpired when handed out. */
  accessToken: string
  /** Always unexpired when handed out. Carries email/name; the access token does not. */
  idToken: string
}

export type CognitoTokenSet = {
  accessToken: string
  idToken: string
  refreshToken: string
  expiresIn: number
}

export function sessionCookieOptions() {
  return {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'lax' as const,
    path: '/',
    maxAge: SESSION_MAX_AGE_SECONDS,
  }
}

const nowEpoch = () => Math.floor(Date.now() / 1000)

// 256 bits from a CSPRNG. crypto.randomUUID() would be only ~122 bits of entropy and is specified
// as a UUID, not as an unguessable bearer credential — which is exactly what this value is.
const newSessionId = () => randomBytes(32).toString('base64url')

type IdTokenClaims = { sub: string; email: string; name?: string }

/**
 * Decodes and validates an ID token's claims.
 *
 * The signature is not verified: the token reaches us over TLS in a direct server-to-server call to
 * Cognito's token endpoint, which OIDC Core §3.1.3.7 accepts in place of signature checking. A
 * token arriving by any other route would have to be verified against the JWKS instead.
 */
function readIdTokenClaims(idToken: string): IdTokenClaims | null {
  try {
    const parts = idToken.split('.')
    if (parts.length !== 3) return null

    const payload = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf-8'))

    if (payload.token_use !== 'id') return null
    if (typeof payload.exp !== 'number' || payload.exp <= nowEpoch()) return null
    if (payload.aud !== process.env.COGNITO_CLIENT_ID) return null

    // Only checkable when the pool id is wired through (deployed stages) — locally there is no
    // Cognito, so no session is ever created to validate.
    const userPoolId = process.env.COGNITO_USER_POOL_ID
    const region = process.env.AWS_REGION ?? process.env.AWS_DEFAULT_REGION
    if (userPoolId && region) {
      if (payload.iss !== `https://cognito-idp.${region}.amazonaws.com/${userPoolId}`) return null
    }

    if (typeof payload.sub !== 'string' || typeof payload.email !== 'string') return null

    return { sub: payload.sub, email: payload.email, name: payload.name }
  } catch {
    return null
  }
}

/**
 * Returns the record with unexpired tokens, refreshing first when they are within the skew window.
 * Returns null when the session can no longer be renewed, having deleted the dead record.
 *
 * Never writes a cookie — see getSession().
 */
async function ensureFreshTokens(record: SessionRecord): Promise<SessionRecord | null> {
  if (record.AccessExpiresAt > nowEpoch() + REFRESH_SKEW_SECONDS) return record

  const refreshed = await refreshAccessToken(record.RefreshToken)

  // Refresh token expired, revoked, or the user was disabled in Cognito — the session is over.
  if (!refreshed) {
    await deleteSessionRecord(record.SessionId)
    return null
  }

  // A refreshed token that identifies a different principal means something is badly wrong
  // (misrouted token, tampered record). Fail closed rather than silently swapping identities.
  const claims = readIdTokenClaims(refreshed.idToken)
  if (!claims || claims.sub !== record.Sub) {
    await deleteSessionRecord(record.SessionId)
    return null
  }

  const next: SessionRecord = {
    ...record,
    AccessToken: refreshed.accessToken,
    IdToken: refreshed.idToken,
    // Cognito returns a new refresh token only when rotation is enabled on the client; without it
    // the response omits the field and the existing token stays valid.
    RefreshToken: refreshed.refreshToken ?? record.RefreshToken,
    AccessExpiresAt: nowEpoch() + refreshed.expiresIn,
  }

  const won = await rotateSessionTokens(next, record.AccessExpiresAt)
  if (won) return next

  // Another request rotated first. Its tokens are the valid ones — ours may already be dead under
  // rotation, so adopt the winner's rather than returning what we just exchanged.
  return await getSessionRecord(record.SessionId)
}

/**
 * The current session, or null when there is none. Refreshes Cognito tokens transparently.
 *
 * Memoized per request: N callers (resolveOwner, getAuthHeaders, /api/auth/me) cost one GetItem.
 *
 * Deliberately performs no cookie writes. Server Components cannot set cookies, and this runs in
 * that context via getAuthHeaders(); more importantly, refreshing without touching a cookie is
 * what makes it safe on cached/SSG routes — the hazard that previously forced refresh to be
 * confined to /api/graphql (ADR-0041 §5). A cookie pointing at a deleted record is inert.
 */
export const getSession = cache(async (): Promise<Session | null> => {
  const store = await cookies()
  const sessionId = store.get(SESSION_COOKIE)?.value
  if (!sessionId) return null

  const record = await getSessionRecord(sessionId)
  if (!record) return null

  const fresh = await ensureFreshTokens(record)
  if (!fresh) return null

  return {
    sessionId: fresh.SessionId,
    sub: fresh.Sub,
    email: fresh.Email,
    name: fresh.Name,
    accessToken: fresh.AccessToken,
    idToken: fresh.IdToken,
  }
})

export async function requireSession(): Promise<Session> {
  const session = await getSession()
  if (!session) throw new Error('Not authenticated')
  return session
}

/**
 * Persists a freshly issued Cognito token set and returns the opaque id for the cookie.
 * Claims are validated here, once — after this, identity is read from the record and no code
 * path derives it by decoding a JWT.
 */
export async function createSession(tokens: CognitoTokenSet): Promise<string> {
  const claims = readIdTokenClaims(tokens.idToken)
  if (!claims) throw new Error('Cognito returned an ID token that failed claim validation')

  const sessionId = newSessionId()
  const createdAt = nowEpoch()

  await putSessionRecord({
    SessionId: sessionId,
    Sub: claims.sub,
    Email: claims.email,
    Name: claims.name,
    AccessToken: tokens.accessToken,
    IdToken: tokens.idToken,
    RefreshToken: tokens.refreshToken,
    AccessExpiresAt: createdAt + tokens.expiresIn,
    CreatedAt: createdAt,
    ExpiresAt: createdAt + SESSION_MAX_AGE_SECONDS,
  })

  return sessionId
}

/**
 * Ends the session server-side. Deleting the record is what actually revokes access — unlike
 * clearing cookies, it does not depend on the client cooperating. The Cognito revoke is
 * best-effort on top, so the refresh token is dead at the IdP too.
 */
export async function destroySession(): Promise<void> {
  const store = await cookies()
  const sessionId = store.get(SESSION_COOKIE)?.value
  if (!sessionId) return

  const record = await getSessionRecord(sessionId)
  await deleteSessionRecord(sessionId)

  if (record) await revokeRefreshToken(record.RefreshToken)
}
