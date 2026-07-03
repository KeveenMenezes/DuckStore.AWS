import { NextResponse } from 'next/server'
import { randomBytes, createHash } from 'node:crypto'

/**
 * Initiates the Cognito Hosted UI PKCE authorization code flow.
 * Generates verifier + challenge server-side, stores them in httpOnly cookies,
 * then redirects the browser to the Cognito login page.
 */
export async function GET(req: Request): Promise<Response> {
  const verifier = randomBytes(32).toString('base64url')
  const challenge = createHash('sha256').update(verifier).digest('base64url')
  const state = randomBytes(16).toString('hex')

  const params = new URLSearchParams({
    client_id: process.env.COGNITO_CLIENT_ID!,
    redirect_uri: `${process.env.NEXT_PUBLIC_SITE_URL}/api/auth/callback`,
    response_type: 'code',
    scope: 'openid email profile',
    code_challenge: challenge,
    code_challenge_method: 'S256',
    state,
  })

  // Both sign-in and sign-up go through /oauth2/authorize. The pool uses Cognito Managed Login,
  // which serves a combined page (sign in + "Create an account") and does NOT expose a standalone
  // /signup deep-link like the classic Hosted UI did — hitting /signup returns "An error was
  // encountered with the requested page". The `?screen=signup` hint is kept but only affects the
  // UX copy, not the endpoint.
  const response = NextResponse.redirect(
    `${process.env.COGNITO_HOSTED_UI_URL}/oauth2/authorize?${params}`,
  )

  // Temporary cookies — expire in 5 minutes (enough for the login flow)
  const cookieOpts = { httpOnly: true, sameSite: 'lax' as const, maxAge: 300, path: '/' }
  response.cookies.set('pkce_verifier', verifier, cookieOpts)
  response.cookies.set('pkce_state', state, cookieOpts)

  return response
}
