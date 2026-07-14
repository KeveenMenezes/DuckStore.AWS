import { DynamoDBClient, ScanCommand } from '@aws-sdk/client-dynamodb';
import {
  DeleteObjectsCommand,
  ListObjectsV2Command,
  S3Client,
} from '@aws-sdk/client-s3';

/**
 * Manual orphan sweep (ADR-0034). Uploads whose form was abandoned leave originals (and
 * possibly processed variants) that no product references. Age alone cannot identify an
 * orphan (a live product's original is just as old), so this cross-references S3 against
 * the products table and only deletes unreferenced imageIds older than MIN_AGE_DAYS —
 * the ULID encodes its creation time, no extra metadata needed.
 *
 * Usage (dry-run by default; pass --delete to actually remove objects):
 *   npx tsx scripts/sweep-orphan-images.ts \
 *     --originals <originals-bucket> --processed <processed-bucket> [--delete]
 */

const MIN_AGE_DAYS = 7;
const ULID_ALPHABET = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

const s3 = new S3Client({});
const dynamo = new DynamoDBClient({});

function argValue(flag: string): string | undefined {
  const index = process.argv.indexOf(flag);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

/** The first 10 ULID chars are a Crockford-base32, 48-bit Unix-ms timestamp. */
function ulidTimestamp(imageId: string): number {
  let ms = 0;
  for (const char of imageId.slice(0, 10)) {
    ms = ms * 32 + ULID_ALPHABET.indexOf(char);
  }
  return ms;
}

async function listImageIds(bucket: string): Promise<Set<string>> {
  const ids = new Set<string>();
  let continuationToken: string | undefined;
  do {
    const page = await s3.send(
      new ListObjectsV2Command({
        Bucket: bucket,
        Prefix: 'images/',
        ContinuationToken: continuationToken,
      }),
    );
    for (const object of page.Contents ?? []) {
      const imageId = object.Key?.split('/')[1];
      if (imageId) ids.add(imageId);
    }
    continuationToken = page.NextContinuationToken;
  } while (continuationToken);
  return ids;
}

async function listReferencedImageIds(): Promise<Set<string>> {
  const referenced = new Set<string>();
  let exclusiveStartKey: Record<string, unknown> | undefined;
  do {
    const page = await dynamo.send(
      new ScanCommand({
        TableName: 'products',
        ProjectionExpression: 'Images',
        ExclusiveStartKey: exclusiveStartKey as never,
      }),
    );
    for (const item of page.Items ?? []) {
      for (const image of item.Images?.L ?? []) {
        const imageId = image.M?.ImageId?.S;
        if (imageId) referenced.add(imageId);
      }
    }
    exclusiveStartKey = page.LastEvaluatedKey;
  } while (exclusiveStartKey);
  return referenced;
}

async function deletePrefix(bucket: string, prefix: string): Promise<number> {
  let deleted = 0;
  let continuationToken: string | undefined;
  do {
    const page = await s3.send(
      new ListObjectsV2Command({ Bucket: bucket, Prefix: prefix, ContinuationToken: continuationToken }),
    );
    const keys = (page.Contents ?? []).flatMap((o) => (o.Key ? [{ Key: o.Key }] : []));
    if (keys.length > 0) {
      await s3.send(new DeleteObjectsCommand({ Bucket: bucket, Delete: { Objects: keys } }));
      deleted += keys.length;
    }
    continuationToken = page.NextContinuationToken;
  } while (continuationToken);
  return deleted;
}

async function main(): Promise<void> {
  const originalsBucket = argValue('--originals');
  const processedBucket = argValue('--processed');
  const shouldDelete = process.argv.includes('--delete');
  if (!originalsBucket || !processedBucket) {
    console.error('Usage: sweep-orphan-images.ts --originals <bucket> --processed <bucket> [--delete]');
    process.exit(1);
  }

  const [uploaded, referenced] = await Promise.all([
    listImageIds(originalsBucket),
    listReferencedImageIds(),
  ]);

  const cutoff = Date.now() - MIN_AGE_DAYS * 24 * 60 * 60 * 1000;
  const orphans = [...uploaded].filter(
    (id) => !referenced.has(id) && ulidTimestamp(id) < cutoff,
  );

  console.log(
    `${uploaded.size} uploaded imageIds, ${referenced.size} referenced by products, ` +
      `${orphans.length} orphans older than ${MIN_AGE_DAYS} days.`,
  );

  for (const imageId of orphans) {
    if (shouldDelete) {
      const fromOriginals = await deletePrefix(originalsBucket, `images/${imageId}/`);
      const fromProcessed = await deletePrefix(processedBucket, `images/${imageId}/`);
      console.log(`deleted ${imageId} (${fromOriginals} original, ${fromProcessed} processed objects)`);
    } else {
      console.log(`[dry-run] would delete images/${imageId}/ from both buckets`);
    }
  }

  if (!shouldDelete && orphans.length > 0) {
    console.log('Re-run with --delete to remove the listed prefixes.');
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
