import { revalidateTag } from 'next/cache'
import { NextResponse } from 'next/server'

// Receiver stub for catalog change events.
// Future source: DynamoDB Stream → Lambda → POST /api/webhooks/catalog-updated
// Required env var: CATALOG_WEBHOOK_SECRET (must match the value sent by the Lambda).
export async function POST(req: Request) {
  const secret = process.env.CATALOG_WEBHOOK_SECRET
  if (!secret) {
    console.error('[webhook] CATALOG_WEBHOOK_SECRET is not set')
    return NextResponse.json({ error: 'Webhook not configured' }, { status: 500 })
  }

  if (req.headers.get('x-webhook-secret') !== secret) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
  }

  revalidateTag('products', {})
  return NextResponse.json({ revalidated: true, tag: 'products' })
}
