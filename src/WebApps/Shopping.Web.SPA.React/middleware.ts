import { NextRequest, NextResponse } from 'next/server'

// The Lambda Function URL behind CloudFront is public (NONE auth) — see
// infra/stacks/spa-stack.ts for why AWS_IAM + OAC doesn't work for our POST
// routes. CloudFront injects x-origin-verify as a custom origin header;
// requests that reach this Lambda without it didn't come through our
// distribution. ORIGIN_VERIFY_SECRET is unset in local dev, so this is a
// no-op outside the deployed Lambda.
//
// Token refresh used to live here, scoped to /api/graphql so its Set-Cookie couldn't be baked
// into an SSG/ISR-cached response. It now lives in lib/auth/session.ts (ADR-0041): tokens are
// held server-side, so renewal mutates no cookie and there is nothing left to leak into a cached
// response — the route scoping it required is gone with it. It also could not have stayed here:
// middleware runs on the Edge runtime, which cannot load the DynamoDB client the session store
// needs.
export async function middleware(request: NextRequest) {
  const secret = process.env.ORIGIN_VERIFY_SECRET
  if (secret && request.headers.get('x-origin-verify') !== secret) {
    return new NextResponse('Forbidden', { status: 403 })
  }

  return NextResponse.next()
}
