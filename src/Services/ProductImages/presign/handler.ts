import { S3Client } from '@aws-sdk/client-s3';
import { createPresignedPost } from '@aws-sdk/s3-presigned-post';
import { ulid } from 'ulid';

/**
 * Issues batch presigned POSTs for direct browser->S3 product image uploads (ADR-0034).
 * Invoked as an AppSync Lambda resolver (Mutation.createProductImageUpload); the resolver
 * has already enforced Cognito + Admin/Seller group. Stateless by design: generates the
 * imageId (ULID) and key here, writes nothing to any database — the S3 object itself is
 * the only state until the product is created.
 */

const CONTENT_TYPE_EXTENSIONS: Record<string, string> = {
  'image/jpeg': 'jpg',
  'image/png': 'png',
  'image/webp': 'webp',
  'image/avif': 'avif',
};

const MAX_UPLOADS_PER_CALL = 12;
const MAX_UPLOAD_BYTES = 8 * 1024 * 1024;
const URL_EXPIRY_SECONDS = 15 * 60;

const s3 = new S3Client({});

interface PresignRequest {
  contentTypes: string[];
}

interface PresignedImageUpload {
  imageId: string;
  url: string;
  fields: Record<string, string>;
}

export const handler = async (event: PresignRequest): Promise<PresignedImageUpload[]> => {
  const bucket = process.env.ORIGINALS_BUCKET;
  if (!bucket) throw new Error('ORIGINALS_BUCKET is not configured.');

  const contentTypes = event.contentTypes ?? [];
  if (contentTypes.length === 0 || contentTypes.length > MAX_UPLOADS_PER_CALL) {
    throw new Error(`contentTypes must contain between 1 and ${MAX_UPLOADS_PER_CALL} entries.`);
  }

  const unsupported = contentTypes.find((ct) => !CONTENT_TYPE_EXTENSIONS[ct]);
  if (unsupported) {
    throw new Error(
      `Unsupported content type "${unsupported}". Allowed: ${Object.keys(CONTENT_TYPE_EXTENSIONS).join(', ')}.`,
    );
  }

  return Promise.all(
    contentTypes.map(async (contentType) => {
      const imageId = ulid();
      const key = `images/${imageId}/original.${CONTENT_TYPE_EXTENSIONS[contentType]}`;

      const { url, fields } = await createPresignedPost(s3, {
        Bucket: bucket,
        Key: key,
        // Exact-match Content-Type comes from Fields; the size cap is why presigned POST
        // is used at all — presigned PUT cannot enforce content-length-range (ADR-0034).
        Fields: { 'Content-Type': contentType },
        Conditions: [['content-length-range', 1, MAX_UPLOAD_BYTES]],
        Expires: URL_EXPIRY_SECONDS,
      });

      return { imageId, url, fields };
    }),
  );
};
