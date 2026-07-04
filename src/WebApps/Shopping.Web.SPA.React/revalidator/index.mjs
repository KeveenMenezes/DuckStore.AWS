import { DynamoDBClient } from '@aws-sdk/client-dynamodb'
import { DynamoDBDocumentClient, QueryCommand, UpdateCommand } from '@aws-sdk/lib-dynamodb'
import { CloudFrontClient, CreateInvalidationCommand } from '@aws-sdk/client-cloudfront'

// Backend-triggered ISR revalidation, wired via `sst.aws.Bus.subscribe` in
// sst.config.ts. Consumes CatalogUpdatedEvent / ReviewCreatedEvent directly
// off the existing `duckstore-event-bus` (still published to by Catalog,
// which stays on CDK) and marks every tag-cache entry for the affected
// tag(s) as stale directly in DynamoDB — the same mechanism Next.js's own
// revalidateTag() uses internally: it re-writes each {tag, path} row with
// revalidatedAt = now, which the read-side staleness check (a Query against
// the "revalidate" GSI comparing revalidatedAt against the cached entry's
// lastModified) picks up on the next request, forcing a fresh fetch instead
// of serving the cached value.
//
// SST's Nextjs component creates and seeds this DynamoDB table itself as
// part of `sst deploy` — no separate seeder Lambda needed here, unlike the
// hand-rolled CDK version this replaces.
//
// `/` and `/products/[id]` are prerendered ISR routes that come back from
// the origin Lambda with `Cache-Control: s-maxage=31536000` — SST's CDN
// caches that at the edge for up to a year. Marking the DynamoDB tag-cache
// stale only changes what the origin Lambda serves on its *next*
// invocation; it does nothing for a CloudFront edge location that already
// has the page cached. So every stale-tag write here is paired with a
// CloudFront path invalidation for the pages that tag feeds.
//
// Tags are granular per product (products:{id}, reviews:{id} — see
// app/products/[id]/page.tsx) except the home page's blanket "products" tag,
// which every product's data feeds into. CatalogUpdatedEvent carries the
// changed product's Id (from the DynamoDB Streams record key — see
// CatalogStreamEventPublisherFunction.cs), so a catalog change invalidates
// both the home page and that one product's own page, without touching
// every other product's cached page.

const ddb = DynamoDBDocumentClient.from(new DynamoDBClient({}))
const cloudfront = new CloudFrontClient({})

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

// OpenNext's DynamoDB tag cache namespaces every partition key with the
// Next.js build ID — a row's `tag` is stored as "{buildId}/products", not
// "products" (and `path` likewise), so a new deploy's cache can't collide
// with the previous build's. Querying the bare tag matches nothing. The
// build ID is injected at deploy time from .open-next/assets/BUILD_ID (see
// sst.config.ts) — must match what's set on the server function's own
// OPEN_NEXT_BUILD_ID env var (also wired in sst.config.ts), or lookups
// silently match nothing.
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

  const paths = [...new Set(tags.flatMap(pathsForTag))]

  await Promise.all([...tags.map(markTagStale), invalidatePaths(paths)])
}
