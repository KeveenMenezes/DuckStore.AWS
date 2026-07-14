import { NextResponse } from 'next/server'
import { cookies } from 'next/headers'

type AuthUser = { sub: string; email: string; username: string; name?: string }

/**
 * Returns the current user's identity decoded from the ID Token cookie, or an
 * explicit "not authenticated" state when there is no session.
 *
 * Always responds 200: asking "who am I?" and getting "nobody" is a valid
 * answer, not a failure — returning 401 for the (normal) logged-out first visit
 * only produced a red error in the browser console for an expected state.
 * Callers branch on the `authenticated` field of the body, not the HTTP status.
 * (401 stays reserved for actually rejecting bad credentials, which this
 * endpoint never receives.)
 *
 * The ID Token was signed by Cognito and validated during the callback flow;
 * we only decode the payload here (no re-verification needed since it came from
 * the server-set httpOnly cookie, not from the client).
 */
export async function GET(): Promise<Response> {
  const cookieStore = await cookies()
  const idToken = cookieStore.get('id_token')?.value

  if (!idToken) {
    return NextResponse.json({ authenticated: false, user: null })
  }

  try {
    const parts = idToken.split('.')
    if (parts.length !== 3) throw new Error('malformed token')
    const payload = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf-8'))

    const user: AuthUser = {
      sub: payload.sub as string,
      email: payload.email as string,
      username: (payload['cognito:username'] ?? payload.email) as string,
      // The `name` claim is now captured at sign-up (fullname is a required
      // attribute). It's the human-friendly display name; `cognito:username` is a UUID.
      name: payload.name as string | undefined,
    }
    return NextResponse.json({ authenticated: true, user })
  } catch {
    // A malformed/undecodable cookie is effectively no session — report it as
    // unauthenticated instead of surfacing an error.
    return NextResponse.json({ authenticated: false, user: null })
  }
}
