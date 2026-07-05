import { createHmac } from 'crypto'
import { CloudFrontClient, CreateInvalidationCommand } from '@aws-sdk/client-cloudfront'

// Backend-triggered ISR revalidation, wired via `sst.aws.Bus.subscribe` in
// sst.config.ts. Consumes CatalogUpdatedEvent / ReviewCreatedEvent directly
// off the existing `duckstore-event-bus` (still published to by Catalog,
// which stays on CDK).
//
// Calls the SPA's single generic webhook (app/api/webhooks/revalidate/route.ts),
// which calls Next.js's real revalidateTag() — instead of writing to the
// OpenNext DynamoDB tag-cache table directly. That table's schema/env vars
// were reverse-engineered from a minified bundle; the public revalidateTag()
// API is the maintained, documented mechanism.
//
// Requests are signed with HMAC-SHA256 over `${timestamp}.${body}` (like
// Stripe/GitHub webhooks) instead of sending a shared secret as a plain
// header — the secret itself never goes over the wire, and the timestamp
// keeps a captured request from being replayed indefinitely.
//
// revalidateTag() alone doesn't touch CloudFront — it only marks the
// DynamoDB tag-cache stale, which changes what the origin Lambda serves on
// its *next* invocation. `/` and `/products/[id]` are prerendered ISR routes
// cached at the CloudFront edge for up to a year (`s-maxage=31536000`), so a
// PoP that already has the page cached would keep serving it regardless.
// Hence the explicit CreateInvalidation call below, for the same paths the
// webhook just revalidated.

const cloudfront = new CloudFrontClient({})

function tagsForEvent(event) {
  const detailType = event['detail-type']
  const productId = event.detail?.ProductId
  if (detailType === 'CatalogUpdatedEvent') {
    return productId ? ['products', `products:${productId}`] : ['products']
  }
  if (detailType === 'ReviewCreatedEvent') {
    return productId ? [`reviews:${productId}`] : []
  }
  return []
}

// Paths whose cached HTML/RSC depends on a given tag, mirroring the
// products/{id} and reviews/{id} tags used by app/products/[id]/page.tsx and
// the blanket "products" tag used by app/page.tsx.
function pathsForTag(tag) {
  if (tag === 'products') return ['/']
  const productMatch = /^products:(.+)$/.exec(tag)
  if (productMatch) return [`/products/${productMatch[1]}`]
  const reviewMatch = /^reviews:(.+)$/.exec(tag)
  if (reviewMatch) return [`/products/${reviewMatch[1]}`]
  return []
}

function withRscVariants(paths) {
  return paths.flatMap((path) => [path, `${path}?_rsc=*`])
}

async function callRevalidateWebhook(tags) {
  const body = JSON.stringify({ tags })
  const timestamp = Date.now().toString()
  const signature = createHmac('sha256', process.env.WEBHOOK_SECRET).update(`${timestamp}.${body}`).digest('hex')

  const response = await fetch(`${process.env.SPA_URL}/api/webhooks/revalidate`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-webhook-timestamp': timestamp,
      'x-webhook-signature': `sha256=${signature}`,
    },
    body,
  })

  if (!response.ok) {
    throw new Error(`revalidate webhook responded ${response.status}: ${await response.text()}`)
  }

  console.log('Called revalidate webhook', await response.json())
}

async function invalidatePaths(paths) {
  if (paths.length === 0) return

  await cloudfront.send(
    new CreateInvalidationCommand({
      DistributionId: process.env.CLOUDFRONT_DISTRIBUTION_ID,
      InvalidationBatch: {
        CallerReference: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
        Paths: { Quantity: paths.length, Items: paths },
      },
    }),
  )

  console.log(`Invalidated CloudFront path(s): ${paths.join(', ')}`)
}

export const handler = async (event) => {
  const detailType = event['detail-type']
  const tags = tagsForEvent(event)
  if (tags.length === 0) {
    console.warn(`No tags derived for detail-type "${detailType}", skipping`)
    return
  }

  const paths = withRscVariants([...new Set(tags.flatMap(pathsForTag))])

  await callRevalidateWebhook(tags)
  await invalidatePaths(paths)
}
