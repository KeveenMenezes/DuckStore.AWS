import { NextRequest, NextResponse } from 'next/server'
import { cookies } from 'next/headers'
import { REFRESH_TOKEN_COOKIE } from '@/lib/cognito-refresh'

/**
 * Clears the auth cookies and redirects to the Cognito logout endpoint so the
 * Cognito session is also invalidated (prevents SSO re-login without credentials).
 */
export async function GET(req: NextRequest): Promise<Response> {
  const cookieStore = await cookies()
  const refreshToken = cookieStore.get(REFRESH_TOKEN_COOKIE)?.value

  // Revoke the refresh token server-side so middleware.ts's silent refresh can't
  // resurrect the session after logout. Best-effort: a failed revoke must not
  // block logout — the token simply expires naturally after 30 days.
  if (refreshToken) {
    try {
      await fetch(`${process.env.COGNITO_HOSTED_UI_URL}/oauth2/revoke`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          token: refreshToken,
          client_id: process.env.COGNITO_CLIENT_ID!,
        }),
      })
    } catch {
      // Cognito unreachable — cookies are still cleared below.
    }
  }

  const logoutUrl = `${process.env.COGNITO_HOSTED_UI_URL}/logout?client_id=${process.env.COGNITO_CLIENT_ID}&logout_uri=${encodeURIComponent(process.env.NEXT_PUBLIC_SITE_URL ?? new URL('/', req.url).toString())}`

  const response = NextResponse.redirect(logoutUrl)
  response.cookies.delete('access_token')
  response.cookies.delete('id_token')
  response.cookies.delete(REFRESH_TOKEN_COOKIE)
  return response
}
