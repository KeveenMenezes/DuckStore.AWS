import {
  CopyObjectCommand,
  DeleteObjectCommand,
  GetObjectCommand,
  PutObjectCommand,
  S3Client,
} from '@aws-sdk/client-s3';
import type { S3Event } from 'aws-lambda';
import type { SQSBatchItemFailure, SQSBatchResponse, SQSEvent } from 'aws-lambda';
import sharp from 'sharp';

/**
 * Product image processor (ADR-0034). Consumes S3 ObjectCreated notifications from SQS,
 * validates the original by its actual content (the POST policy's Content-Type is
 * declarative, not proof), and writes the full variant matrix to the processed bucket:
 * 5 widths x AVIF/WebP/JPEG, each with an immutable Cache-Control header. Deliberately
 * database-free: output keys are deterministic (images/{imageId}/{size}.{fmt}), so a
 * retry overwrites the same objects and idempotency needs no bookkeeping. Invalid files
 * are moved to quarantine/ and acknowledged — retrying them cannot succeed.
 */

const VARIANT_WIDTHS = [160, 320, 640, 1024, 1600];
const VALID_FORMATS = new Set(['jpeg', 'png', 'webp', 'avif']);
const CACHE_CONTROL = 'public, max-age=31536000, immutable';

const s3 = new S3Client({});

export const handler = async (event: SQSEvent): Promise<SQSBatchResponse> => {
  const batchItemFailures: SQSBatchItemFailure[] = [];

  for (const record of event.Records) {
    try {
      const body = JSON.parse(record.body);
      // S3 emits a subscription-confirmation TestEvent when the notification is wired up.
      if (body.Event === 's3:TestEvent') continue;

      for (const s3Record of (body as S3Event).Records ?? []) {
        await processObject(
          s3Record.s3.bucket.name,
          decodeURIComponent(s3Record.s3.object.key.replace(/\+/g, ' ')),
        );
      }
    } catch (error) {
      console.error(`Failed to process message ${record.messageId}`, error);
      batchItemFailures.push({ itemIdentifier: record.messageId });
    }
  }

  return { batchItemFailures };
};

async function processObject(sourceBucket: string, key: string): Promise<void> {
  const processedBucket = process.env.PROCESSED_BUCKET;
  if (!processedBucket) throw new Error('PROCESSED_BUCKET is not configured.');

  // Key contract from the presign Lambda: images/{imageId}/original.{ext}
  const [prefix, imageId] = key.split('/');
  if (prefix !== 'images' || !imageId) {
    console.warn(`Ignoring object with unexpected key shape: ${key}`);
    return;
  }

  const original = await s3.send(new GetObjectCommand({ Bucket: sourceBucket, Key: key }));
  const buffer = Buffer.from(await original.Body!.transformToByteArray());

  if (!(await isSupportedImage(buffer))) {
    await quarantine(sourceBucket, key);
    return;
  }

  for (const width of VARIANT_WIDTHS) {
    // rotate() bakes in the EXIF orientation, and re-encoding drops all metadata
    // (camera GPS must never reach the CDN). One resize per width, three encodes
    // from the same resized pipeline.
    const resized = sharp(buffer).rotate().resize({ width, withoutEnlargement: true });

    const [avif, webp, jpeg] = await Promise.all([
      resized.clone().avif({ quality: 55 }).toBuffer(),
      resized.clone().webp({ quality: 75 }).toBuffer(),
      resized.clone().jpeg({ quality: 80, mozjpeg: true }).toBuffer(),
    ]);

    await Promise.all([
      putVariant(processedBucket, imageId, width, 'avif', 'image/avif', avif),
      putVariant(processedBucket, imageId, width, 'webp', 'image/webp', webp),
      putVariant(processedBucket, imageId, width, 'jpg', 'image/jpeg', jpeg),
    ]);
  }
}

async function isSupportedImage(buffer: Buffer): Promise<boolean> {
  try {
    // sharp derives the format from the actual bytes — this is the magic-byte check.
    const { format } = await sharp(buffer).metadata();
    return format !== undefined && VALID_FORMATS.has(format);
  } catch {
    return false;
  }
}

async function quarantine(bucket: string, key: string): Promise<void> {
  console.warn(`Quarantining non-image upload: ${key}`);
  await s3.send(
    new CopyObjectCommand({
      Bucket: bucket,
      CopySource: `${bucket}/${encodeURIComponent(key)}`,
      Key: key.replace(/^images\//, 'quarantine/'),
    }),
  );
  await s3.send(new DeleteObjectCommand({ Bucket: bucket, Key: key }));
}

async function putVariant(
  bucket: string,
  imageId: string,
  width: number,
  extension: string,
  contentType: string,
  body: Buffer,
): Promise<void> {
  await s3.send(
    new PutObjectCommand({
      Bucket: bucket,
      Key: `images/${imageId}/${width}.${extension}`,
      Body: body,
      ContentType: contentType,
      CacheControl: CACHE_CONTROL,
    }),
  );
}
