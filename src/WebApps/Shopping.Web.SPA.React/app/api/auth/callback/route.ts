import { NextRequest, NextResponse } from 'next/server'
import { cookies } from 'next/headers'
import { GUEST_COOKIE } from '@/lib/identity'
import { MERGE_BASKET } from '@/api/mutations/order'
import { createSession, SESSION_COOKIE, sessionCookieOptions } from '@/lib/auth/session'

// Only same-origin relative paths are accepted (rejects "//host", "http://host", etc.)
// to avoid turning the stored redirect into an open redirect.
function sanitizeReturnTo(next: string | null | undefined): string | null {
  if (!next || !/^\/(?!\/)/.test(next)) return null
  return next
}

/**
 * Merges the visitor's GUEST# cart into the just-authenticated USER# cart. Best-effort:
 * a merge failure must not block login. Goes through this app's own /api/graphql (same
 * endpoint every other GraphQL call uses, whichever GRAPHQL_BACKEND is active) instead of
 * calling AppSync directly, so it also works locally against the Yoga backend — not just
 * when APPSYNC_URL is set. The session/guest cookies aren't on this request (the browser
 * doesn't have them yet), so we forward them explicitly via a synthesized Cookie header —
 * resolveOwner()/prepareBasketRequest resolve identity from it exactly as they would for any
 * browser-originated request, and getAuthHeaders() loads the ID token AppSync needs from the
 * same session. This is why the session record must already be persisted before this runs.
 */
async function mergeGuestCart(siteUrl: string, sessionId: string, guestId: string): Promise<void> {
  try {
    const res = await fetch(new URL('/api/graphql', siteUrl), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Cookie: `${SESSION_COOKIE}=${sessionId}; ${GUEST_COOKIE}=${guestId}`,
      },
      body: JSON.stringify({
        query: MERGE_BASKET,
        variables: { guestId: `GUEST#${guestId}` },
      }),
    })
    const json = (await res.json()) as { errors?: Array<{ message: string }> }
    if (json.errors?.length) {
      console.error('mergeBasket returned errors', json.errors)
    }
  } catch (error) {
    // Swallow — the user is logged in; the guest cart simply isn't merged. Logged so a
    // regression here (like the missing id_token that caused this to always fail) is visible.
    console.error('Failed to merge guest cart on login', error)
  }
}

/**
 * Receives the authorization code from Cognito, exchanges it for tokens (PKCE flow,
 * server-side), and opens a server-side session — the browser receives only the opaque
 * session id. No Cognito token is ever sent to the client (ADR-0041).
 */
export async function GET(req: NextRequest): Promise<Response> {
  const { searchParams } = new URL(req.url)
  const code = searchParams.get('code')
  const state = searchParams.get('state')

  // Behind CloudFront the Host header is stripped (ALL_VIEWER_EXCEPT_HOST_HEADER),
  // so req.url resolves to the internal Lambda Function URL host. Redirecting
  // against it would send the browser straight to the Function URL — bypassing
  // CloudFront (no x-origin-verify header) and getting a 403 from middleware.ts.
  // Always redirect against the public site URL instead.
  const siteUrl = process.env.NEXT_PUBLIC_SITE_URL ?? req.url

  const cookieStore = await cookies()
  const storedState = cookieStore.get('pkce_state')?.value
  const verifier = cookieStore.get('pkce_verifier')?.value
  const guestId = cookieStore.get(GUEST_COOKIE)?.value
  const returnTo = sanitizeReturnTo(cookieStore.get('post_login_redirect')?.value)

  if (!code || !state || state !== storedState || !verifier) {
    return NextResponse.redirect(new URL('/?auth_error=invalid_state', siteUrl))
  }

  const tokenRes = await fetch(`${process.env.COGNITO_HOSTED_UI_URL}/oauth2/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: process.env.COGNITO_CLIENT_ID!,
      code,
      redirect_uri: `${process.env.NEXT_PUBLIC_SITE_URL}/api/auth/callback`,
      code_verifier: verifier,
    }),
  })

  if (!tokenRes.ok) {
    return NextResponse.redirect(new URL('/?auth_error=token_exchange_failed', siteUrl))
  }

  const { access_token, id_token, refresh_token, expires_in } = (await tokenRes.json()) as {
    access_token: string
    id_token: string
    refresh_token: string
    expires_in: number
  }

  let sessionId: string
  try {
    sessionId = await createSession({
      accessToken: access_token,
      idToken: id_token,
      refreshToken: refresh_token,
      expiresIn: expires_in,
    })
  } catch (error) {
    // Claim validation failed, or the session table is unavailable. Either way there is no
    // session to hand out — better a clean failed login than a half-authenticated state.
    console.error('Failed to create session after token exchange', error)
    return NextResponse.redirect(new URL('/?auth_error=session_creation_failed', siteUrl))
  }

  // Merge the guest cart into the new session before clearing the guest cookie (login → merge).
  // Runs after createSession() so the synthesized session cookie resolves to a real record.
  if (guestId) {
    await mergeGuestCart(siteUrl, sessionId, guestId)
  }

  const response = NextResponse.redirect(new URL(returnTo ?? '/', siteUrl))
  response.cookies.set(SESSION_COOKIE, sessionId, sessionCookieOptions())
  response.cookies.delete('pkce_verifier')
  response.cookies.delete('pkce_state')
  response.cookies.delete('post_login_redirect')
  // Guest identity is now merged into the user cart — retire the guest cookie.
  if (guestId) response.cookies.delete(GUEST_COOKIE)

  return response
}
