import { DynamoDBClient } from '@aws-sdk/client-dynamodb'
import { DynamoDBDocumentClient, QueryCommand } from '@aws-sdk/lib-dynamodb'
import { SQSClient, SendMessageCommand } from '@aws-sdk/client-sqs'

// Replaces the old HTTP webhook (public POST + shared secret) for
// backend-triggered ISR revalidation. Consumes CatalogUpdatedEvent /
// ReviewCreatedEvent directly off EventBridge, resolves affected paths via
// OpenNext's tag cache table (schema verified against the actual bundled
// Lambda code, not guessed — see spa-storage.ts), and enqueues them onto the
// same SQS FIFO queue OpenNext's own revalidation function already consumes.
// No public HTTP surface, no secret to manage.

const ddb = DynamoDBDocumentClient.from(new DynamoDBClient({}))
const sqs = new SQSClient({})

const TAG_BY_DETAIL_TYPE = {
  CatalogUpdatedEvent: 'products',
  ReviewCreatedEvent: 'reviews',
}

export const handler = async (event) => {
  const tag = TAG_BY_DETAIL_TYPE[event['detail-type']]
  if (!tag) {
    console.warn(`Unrecognized detail-type "${event['detail-type']}", skipping`)
    return
  }

  const { Items = [] } = await ddb.send(
    new QueryCommand({
      TableName: process.env.TAG_CACHE_TABLE_NAME,
      KeyConditionExpression: '#tag = :tag',
      ExpressionAttributeNames: { '#tag': 'tag' },
      ExpressionAttributeValues: { ':tag': tag },
    }),
  )

  const host = process.env.SPA_HOST

  await Promise.all(
    Items.map((item) =>
      sqs.send(
        new SendMessageCommand({
          QueueUrl: process.env.REVALIDATION_QUEUE_URL,
          MessageBody: JSON.stringify({ host, url: item.path }),
          // contentBasedDeduplication is on for this queue, so no
          // MessageDeduplicationId needed. Grouping by path serializes
          // duplicate revalidations of the same page while letting
          // different pages revalidate in parallel.
          MessageGroupId: String(item.path).slice(0, 128),
        }),
      ),
    ),
  )

  console.log(`Enqueued ${Items.length} path(s) for tag "${tag}" revalidation`)
}
