import { NextRequest, NextResponse } from 'next/server'
import {
  ACCESS_TOKEN_COOKIE,
  ID_TOKEN_COOKIE,
  REFRESH_TOKEN_COOKIE,
  refreshAccessToken,
} from '@/lib/cognito-refresh'

// The Lambda Function URL behind CloudFront is public (NONE auth) — see
// infra/stacks/spa-stack.ts for why AWS_IAM + OAC doesn't work for our POST
// routes. CloudFront injects x-origin-verify as a custom origin header;
// requests that reach this Lambda without it didn't come through our
// distribution. ORIGIN_VERIFY_SECRET is unset in local dev, so this is a
// no-op outside the deployed Lambda.
export async function middleware(request: NextRequest) {
  const secret = process.env.ORIGIN_VERIFY_SECRET
  if (secret && request.headers.get('x-origin-verify') !== secret) {
    return new NextResponse('Forbidden', { status: 403 })
  }

  const hasAccessToken = request.cookies.has(ACCESS_TOKEN_COOKIE)
  const refreshToken = request.cookies.get(REFRESH_TOKEN_COOKIE)?.value

  // access_token/id_token are 1h-lived (SpaClient in infra/constructs/appsync-auth.ts).
  // Without this, the first request after they expire has neither cookie, and
  // resolveOwner() (lib/identity.ts) mints a brand-new GUEST# identity — silently
  // orphaning the signed-in user's cart in DynamoDB. Refresh here, once, before
  // any route handler resolves identity from the cookies.
  if (!hasAccessToken && refreshToken) {
    const refreshed = await refreshAccessToken(refreshToken)
    const isSecure = process.env.NODE_ENV === 'production'
    const cookieOpts = { httpOnly: true, secure: isSecure, sameSite: 'lax' as const, path: '/' }

    if (refreshed) {
      // Forward the refreshed tokens on the *incoming* request too, so this same
      // request's route handler (e.g. resolveOwner()) sees the user's real
      // identity instead of the stale/absent cookie it arrived with.
      const requestHeaders = new Headers(request.headers)
      const forwardedCookie = [
        request.headers.get('cookie'),
        `${ACCESS_TOKEN_COOKIE}=${refreshed.accessToken}`,
        `${ID_TOKEN_COOKIE}=${refreshed.idToken}`,
      ]
        .filter(Boolean)
        .join('; ')
      requestHeaders.set('cookie', forwardedCookie)

      const response = NextResponse.next({ request: { headers: requestHeaders } })
      response.cookies.set(ACCESS_TOKEN_COOKIE, refreshed.accessToken, {
        ...cookieOpts,
        maxAge: refreshed.expiresIn,
      })
      response.cookies.set(ID_TOKEN_COOKIE, refreshed.idToken, {
        ...cookieOpts,
        maxAge: refreshed.expiresIn,
      })
      return response
    }

    // Refresh token itself is expired/revoked — drop it so we stop retrying on
    // every request and fall back to the guest identity cleanly.
    const response = NextResponse.next()
    response.cookies.delete(REFRESH_TOKEN_COOKIE)
    return response
  }

  return NextResponse.next()
}
