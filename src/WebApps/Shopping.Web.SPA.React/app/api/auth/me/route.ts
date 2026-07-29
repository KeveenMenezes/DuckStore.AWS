import { NextResponse } from 'next/server'
import { getSession } from '@/lib/auth/session'

type AuthUser = { sub: string; email: string; username: string; name?: string }

/**
 * Returns the current user's identity, or an explicit "not authenticated" state when there is
 * no session.
 *
 * Identity comes from the server-side session (ADR-0041), which renews expired Cognito tokens
 * transparently. This is what stops the user from being reported as signed out an hour after
 * login: the answer tracks the session's 30-day lifetime, not a 1h credential's.
 *
 * Responds 200 for both answers: asking "who am I?" and getting "nobody" is a
 * valid answer, not a failure — returning 401 for the (normal) logged-out first
 * visit only produced a red error in the browser console for an expected state.
 * Callers branch on the `authenticated` field of the body, not the HTTP status.
 * (401 stays reserved for actually rejecting bad credentials, which this
 * endpoint never receives.)
 *
 * A 5xx is a third, distinct answer: "couldn't determine". It surfaces when
 * Cognito can't be reached to renew the tokens, and it deliberately does NOT
 * mean signed out — the session record is intact and the next attempt should
 * succeed. Answering `authenticated: false` there would report a live session
 * as ended, which is the confusion this endpoint exists to avoid.
 */
// This response varies per session cookie — it must never be cached by the
// CDN/edge (CloudFront/OpenNext) or the previous caller's identity leaks to
// the next request that hits the same cached entry.
export const dynamic = 'force-dynamic'

const NO_STORE_HEADERS = { 'Cache-Control': 'private, no-store, must-revalidate' }

export async function GET(): Promise<Response> {
  const session = await getSession()

  if (!session) {
    return NextResponse.json({ authenticated: false, user: null }, { headers: NO_STORE_HEADERS })
  }

  const user: AuthUser = {
    sub: session.sub,
    email: session.email,
    // `name` is captured at sign-up (fullname is a required attribute). It's the human-friendly
    // display name; the Cognito username is a UUID, so email is the better fallback.
    username: session.email,
    name: session.name,
  }

  return NextResponse.json({ authenticated: true, user }, { headers: NO_STORE_HEADERS })
}
