import { createHmac } from 'crypto'
import { CloudFrontClient, CreateInvalidationCommand } from '@aws-sdk/client-cloudfront'

// Backend-triggered ISR revalidation, wired via `sst.aws.Bus.subscribe` in
// sst.config.ts. Consumes CatalogViewProductSyncedEvent / CatalogViewProductDeletedEvent
// (ADR-0035) off the existing `duckstore-event-bus` for the products/products:{id} tags —
// CatalogView's own CDC events, emitted only after its catalogview-products write commits, rather
// than subscribing directly to the upstream Catalog/Pricing events that feed CatalogView. That
// direct-subscription design raced CatalogView's own consumers with no ordering guarantee, so
// CloudFront could be invalidated (and the ISR page regenerated) before CatalogView had actually
// applied the change, caching stale data behind a "just revalidated" cache-control header for up
// to a year. Since any catalogview-products write (product edit, price change, rating change,
// category rename) produces one of these two events, this also closes the previous gap where
// price changes never triggered revalidation at all.
//
// Also consumes ReviewCreatedEvent / ReviewUpdatedEvent (ADR-0029) directly for the reviews:{id}
// tag — the raw review list lives in Review's own store, not catalogview-products (CatalogView
// only folds in the aggregate rating), so there is no CatalogView event for that data and no race
// to fix on this path: Review's own CDC event already fires only after Review's write commits.
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
  if (
    detailType === 'CatalogViewProductSyncedEvent' ||
    detailType === 'CatalogViewProductDeletedEvent'
  ) {
    return productId ? ['products', `products:${productId}`] : ['products']
  }
  if (detailType === 'ReviewCreatedEvent' || detailType === 'ReviewUpdatedEvent') {
    return productId ? [`reviews:${productId}`] : []
  }
  return []
}

// Paths whose cached HTML/RSC depends on a given tag, mirroring the
// products/{id} and reviews/{id} tags used by app/products/[id]/page.tsx and
// the blanket "products" tag used by app/page.tsx.
function pathsForTag(tag) {
  // /sitemap.xml enumerates the catalog under the same `products` tag (app/sitemap.ts), so it goes
  // stale on exactly the same events as the home page.
  if (tag === 'products') return ['/', '/sitemap.xml']
  const productMatch = /^products:(.+)$/.exec(tag)
  if (productMatch) return [`/products/${productMatch[1]}`]
  const reviewMatch = /^reviews:(.+)$/.exec(tag)
  if (reviewMatch) return [`/products/${reviewMatch[1]}`]
  return []
}

function withRscVariants(paths) {
  // Only navigable routes are ever requested as an RSC payload; /sitemap.xml is a plain file, so
  // pairing it with a ?_rsc=* wildcard would just spend an invalidation path that matches nothing.
  return paths.flatMap((path) =>
    path.includes('.') ? [path] : [path, `${path}?_rsc=*`],
  )
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
