import { NextRequest, NextResponse } from 'next/server'
import { destroySession, SESSION_COOKIE } from '@/lib/auth/session'

/**
 * Ends the session and redirects to the Cognito logout endpoint so the Cognito
 * session is also invalidated (prevents SSO re-login without credentials).
 *
 * Deleting the session record is the authoritative revocation — it cannot be undone by a
 * client that ignores Set-Cookie, and no silent refresh can resurrect it. Clearing the cookie
 * below is only tidiness (ADR-0041 §7).
 */
export async function GET(req: NextRequest): Promise<Response> {
  await destroySession()

  const logoutUrl = `${process.env.COGNITO_HOSTED_UI_URL}/logout?client_id=${process.env.COGNITO_CLIENT_ID}&logout_uri=${encodeURIComponent(process.env.NEXT_PUBLIC_SITE_URL ?? new URL('/', req.url).toString())}`

  const response = NextResponse.redirect(logoutUrl)
  // Must match the `path` the cookie was set with in callback/route.ts — without it,
  // delete() defaults to the request's own path (/api/auth) and leaves the real
  // Path=/ cookie untouched.
  response.cookies.delete({ name: SESSION_COOKIE, path: '/' })
  return response
}
