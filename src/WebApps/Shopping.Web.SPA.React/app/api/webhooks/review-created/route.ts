import { revalidateTag } from 'next/cache'
import { NextResponse } from 'next/server'

// Invalidates the ISR cache for the reviews tag.
// Triggered in two ways:
//   1. Client-side: review-form.tsx POSTs here immediately after a successful createReview mutation.
//   2. Future server-side: DynamoDB Stream (reviews table) → Lambda → POST /api/webhooks/review-created
//      (same CDC pattern as catalog-updated). Provides server-authoritative invalidation for
//      reviews created outside the SPA (e.g. via AppSync Console or API).
// Required env var: REVIEW_WEBHOOK_SECRET
export async function POST(req: Request) {
  const secret = process.env.REVIEW_WEBHOOK_SECRET
  if (!secret) {
    console.error('[webhook] REVIEW_WEBHOOK_SECRET is not set')
    return NextResponse.json({ error: 'Webhook not configured' }, { status: 500 })
  }

  if (req.headers.get('x-webhook-secret') !== secret) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
  }

  revalidateTag('reviews', {})
  return NextResponse.json({ revalidated: true, tag: 'reviews' })
}
