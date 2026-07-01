import { DynamoDBClient } from '@aws-sdk/client-dynamodb'
import { DynamoDBDocumentClient, QueryCommand, UpdateCommand } from '@aws-sdk/lib-dynamodb'

// Replaces the old HTTP webhook (public POST + shared secret) for
// backend-triggered ISR revalidation. Consumes CatalogUpdatedEvent /
// ReviewCreatedEvent directly off EventBridge and marks every tag-cache
// entry for the affected tag(s) as stale, directly in DynamoDB — the same
// mechanism Next.js's own revalidateTag() uses internally (confirmed by
// reading the actual bundled cache handler code, not guessed): it re-writes
// each {tag, path} row with revalidatedAt = now, which the read-side
// staleness check (a Query against the "revalidate" GSI comparing
// revalidatedAt against the cached entry's lastModified) picks up on the
// next request, forcing a fresh fetch instead of serving the cached value.
// Crucially, those partition keys are build-ID-prefixed ("{buildId}/products",
// not "products") — see BUILD_ID below; querying the bare tag matches nothing.
//
// The SQS revalidation queue (OpenNext's own, consumed by the separate
// revalidation-function Lambda) is deliberately NOT used here — it expects
// real page routes (e.g. "/", "/products/123") and only forces regenerating
// already-cached PAGE-level entries. `/` and `/products/[id]` in this app
// are fully dynamic (SSR) routes per next build's own prerender-manifest.json
// (they appear in neither `routes` nor `dynamicRoutes`), so there is no
// PAGE-level cache to regenerate — only the underlying tagged fetch() calls
// are cacheable, which is exactly what tag-cache "path" entries key off of
// (a hash of the fetch's URL+method+headers+body, not a page route).
//
// Tags are granular per product (products:{id}, reviews:{id} — see
// app/products/[id]/page.tsx) except the home page's blanket "products" tag,
// which every product's data feeds into. CatalogUpdatedEvent carries the
// changed product's Id (from the DynamoDB Streams record key — see
// CatalogStreamEventPublisherFunction.cs), so a catalog change invalidates
// both the home page and that one product's own page, without touching
// every other product's cached page.

const ddb = DynamoDBDocumentClient.from(new DynamoDBClient({}))

// OpenNext's DynamoDB tag cache namespaces every partition key with the
// Next.js build ID — a row's `tag` is stored as "{buildId}/products", not
// "products" (and `path` likewise), so a new deploy's cache can't collide
// with the previous build's. Querying the bare tag matches nothing, which
// silently made every revalidation a no-op. The build ID is injected at
// deploy time from .open-next/assets/BUILD_ID (see spa-tag-revalidator.ts).
const BUILD_ID = process.env.TAG_CACHE_BUILD_ID

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

async function markTagStale(tag) {
  const key = BUILD_ID ? `${BUILD_ID}/${tag}` : tag

  const { Items = [] } = await ddb.send(
    new QueryCommand({
      TableName: process.env.TAG_CACHE_TABLE_NAME,
      KeyConditionExpression: '#tag = :tag',
      ExpressionAttributeNames: { '#tag': 'tag' },
      ExpressionAttributeValues: { ':tag': key },
    }),
  )

  const now = Date.now()

  await Promise.all(
    Items.map((item) =>
      ddb.send(
        new UpdateCommand({
          TableName: process.env.TAG_CACHE_TABLE_NAME,
          // item.path is already build-ID-prefixed (read back from the query).
          Key: { tag: key, path: item.path },
          UpdateExpression: 'SET revalidatedAt = :now',
          ExpressionAttributeValues: { ':now': now },
        }),
      ),
    ),
  )

  console.log(`Marked ${Items.length} entr${Items.length === 1 ? 'y' : 'ies'} stale for tag "${key}"`)
}

export const handler = async (event) => {
  const tags = tagsForEvent(event)
  if (tags.length === 0) {
    console.warn(`No tags derived for detail-type "${event['detail-type']}", skipping`)
    return
  }

  await Promise.all(tags.map(markTagStale))
}
