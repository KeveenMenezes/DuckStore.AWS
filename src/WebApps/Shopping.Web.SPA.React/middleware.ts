import { NextRequest, NextResponse } from 'next/server'

// The Lambda Function URL behind CloudFront is public (NONE auth) — see
// infra/stacks/spa-stack.ts for why AWS_IAM + OAC doesn't work for our POST
// routes. CloudFront injects x-origin-verify as a custom origin header;
// requests that reach this Lambda without it didn't come through our
// distribution. ORIGIN_VERIFY_SECRET is unset in local dev, so this is a
// no-op outside the deployed Lambda.
export function middleware(request: NextRequest) {
  const secret = process.env.ORIGIN_VERIFY_SECRET
  if (secret && request.headers.get('x-origin-verify') !== secret) {
    return new NextResponse('Forbidden', { status: 403 })
  }
  return NextResponse.next()
}
