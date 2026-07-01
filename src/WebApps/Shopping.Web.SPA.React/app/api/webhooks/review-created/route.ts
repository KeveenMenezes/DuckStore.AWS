import { revalidateTag } from 'next/cache'
import { NextResponse } from 'next/server'

// Invalidates the ISR cache for one product's reviews:{productId} tag —
// granular, not the old blanket "reviews" tag, so one product's review
// doesn't revalidate every other product's page.
// Triggered client-side: review-form.tsx POSTs here immediately after a
// successful createReview mutation.
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

  const { productId } = await req.json().catch(() => ({ productId: undefined }))
  if (!productId) {
    return NextResponse.json({ error: 'productId is required' }, { status: 400 })
  }

  const tag = `reviews:${productId}`
  revalidateTag(tag, {})
  return NextResponse.json({ revalidated: true, tag })
}
