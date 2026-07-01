import { NextRequest, NextResponse } from 'next/server'
import { cookies } from 'next/headers'

/**
 * Receives the authorization code from Cognito, exchanges it for tokens
 * (PKCE flow, server-side), and stores the Access Token + ID Token in
 * httpOnly cookies — tokens are never exposed to JavaScript.
 */
export async function GET(req: NextRequest): Promise<Response> {
  const { searchParams } = new URL(req.url)
  const code = searchParams.get('code')
  const state = searchParams.get('state')

  const cookieStore = await cookies()
  const storedState = cookieStore.get('pkce_state')?.value
  const verifier = cookieStore.get('pkce_verifier')?.value

  if (!code || !state || state !== storedState || !verifier) {
    return NextResponse.redirect(new URL('/?auth_error=invalid_state', req.url))
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
    return NextResponse.redirect(new URL('/?auth_error=token_exchange_failed', req.url))
  }

  const { access_token, id_token, expires_in } = (await tokenRes.json()) as {
    access_token: string
    id_token: string
    expires_in: number
  }

  const isSecure = process.env.NODE_ENV === 'production'
  const cookieOpts = {
    httpOnly: true,
    secure: isSecure,
    sameSite: 'lax' as const,
    maxAge: expires_in,
    path: '/',
  }

  const response = NextResponse.redirect(new URL('/', req.url))
  response.cookies.set('access_token', access_token, cookieOpts)
  response.cookies.set('id_token', id_token, cookieOpts)
  response.cookies.delete('pkce_verifier')
  response.cookies.delete('pkce_state')

  return response
}
