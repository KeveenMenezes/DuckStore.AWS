import { NextRequest, NextResponse } from 'next/server'

/**
 * Clears the auth cookies and redirects to the Cognito logout endpoint so the
 * Cognito session is also invalidated (prevents SSO re-login without credentials).
 */
export async function GET(req: NextRequest): Promise<Response> {
  const logoutUrl = `${process.env.COGNITO_HOSTED_UI_URL}/logout?client_id=${process.env.COGNITO_CLIENT_ID}&logout_uri=${encodeURIComponent(process.env.NEXT_PUBLIC_SITE_URL ?? new URL('/', req.url).toString())}`

  const response = NextResponse.redirect(logoutUrl)
  response.cookies.delete('access_token')
  response.cookies.delete('id_token')
  return response
}
