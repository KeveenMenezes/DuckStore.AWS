import { revalidateTag } from 'next/cache'
import { NextResponse } from 'next/server'
import { createHmac, timingSafeEqual } from 'crypto'

// Single generic revalidation webhook, replacing one route per event type
// (catalog-updated, review-created). Callers just say which tags changed —
// this route has no idea (and doesn't need to know) whether that came from
// a catalog change, a review, or anything else added later.
//
// Server-to-server only — called exclusively by the `revalidator` Lambda
// (sst.config.ts), reacting to CatalogUpdatedEvent/ReviewCreatedEvent off
// EventBridge. There is deliberately no browser-triggered path: a
// client-side call would only cover reviews submitted through this exact
// SPA's form, leaving a silent blind spot for reviews created any other way
// (seed data, another channel) — the same category of bug this project
// already hit once, where Catalog updates never reached the CDN because the
// only invalidation trigger was fragile. The event-driven path covers every
// case uniformly, so it's the only one.
//
// Authenticated with an HMAC-SHA256 signature over `${timestamp}.${rawBody}`
// (same idea as Stripe/GitHub webhooks) instead of a plain shared-secret
// header comparison: the secret itself never goes over the wire, the
// timestamp (checked against a 5-minute window) keeps a captured
// request/signature pair from being replayed indefinitely, and signing the
// body means a captured signature is worthless for any other payload.
// Verified with `crypto.timingSafeEqual` so the comparison itself isn't a
// timing side-channel.

const REPLAY_WINDOW_MS = 5 * 60 * 1000

function isAuthorized(req: Request, rawBody: string): boolean {
  const signature = req.headers.get('x-webhook-signature')
  const timestamp = req.headers.get('x-webhook-timestamp')
  const secret = process.env.WEBHOOK_SECRET
  if (!signature || !timestamp || !secret) return false

  const age = Date.now() - Number(timestamp)
  if (!Number.isFinite(age) || age < 0 || age > REPLAY_WINDOW_MS) return false

  const expected = createHmac('sha256', secret).update(`${timestamp}.${rawBody}`).digest()
  const given = Buffer.from(signature.replace(/^sha256=/, ''), 'hex')
  return expected.length === given.length && timingSafeEqual(expected, given)
}

export async function POST(req: Request) {
  const rawBody = await req.text()

  if (!isAuthorized(req, rawBody)) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
  }

  const { tags } = JSON.parse(rawBody || '{}') as { tags?: unknown }
  if (!Array.isArray(tags) || tags.length === 0 || !tags.every((t) => typeof t === 'string')) {
    return NextResponse.json({ error: 'tags (non-empty string[]) is required' }, { status: 400 })
  }

  for (const tag of tags) {
    revalidateTag(tag, {})
  }

  return NextResponse.json({ revalidated: true, tags })
}
