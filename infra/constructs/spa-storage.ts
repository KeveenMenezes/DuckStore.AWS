import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';

export interface SpaStorageProps {
  /** Path to the SPA's `.open-next` build output (from `pnpm build:opennext`). */
  readonly openNextDir: string;
}

/**
 * Storage layer for the OpenNext build output: a single S3 bucket (static
 * assets under `_assets`, ISR cache seed under `_cache` — matches OpenNext's
 * `origins.s3` in `open-next.output.json`), the DynamoDB ISR tag cache, and
 * the SQS FIFO revalidation queue.
 *
 * Schema and key names below come from OpenNext's documented reference
 * implementation (https://opennext.js.org/aws/reference-implementation) and
 * from inspecting the actual bundled Lambda code in `.open-next/*-function`,
 * not from guesswork.
 */
export class SpaStorage extends Construct {
  public readonly assetsBucket: s3.Bucket;
  public readonly tagCacheTable: dynamodb.Table;
  public readonly revalidationQueue: sqs.Queue;

  constructor(scope: Construct, id: string, props: SpaStorageProps) {
    super(scope, id);

    const { openNextDir } = props;

    this.assetsBucket = new s3.Bucket(this, 'AssetsBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    // Static assets (_next/*, public/*) — long-lived cache; served by CloudFront
    // from the `_assets` prefix (originPath in open-next.output.json).
    new s3deploy.BucketDeployment(this, 'DeployAssets', {
      sources: [s3deploy.Source.asset(`${openNextDir}/assets`)],
      destinationBucket: this.assetsBucket,
      destinationKeyPrefix: '_assets',
      cacheControl: [s3deploy.CacheControl.fromString('public,max-age=31536000,immutable')],
      prune: false,
    });

    // Initial ISR cache seed — read directly by the server/revalidation Lambdas
    // via the S3 SDK (CACHE_BUCKET_KEY_PREFIX), never served through CloudFront.
    new s3deploy.BucketDeployment(this, 'DeployCache', {
      sources: [s3deploy.Source.asset(`${openNextDir}/cache`)],
      destinationBucket: this.assetsBucket,
      destinationKeyPrefix: '_cache',
      prune: false,
    });

    // ISR tag cache: base table keyed by (tag, path); GSI "revalidate" keyed by
    // (path, revalidatedAt) lets the server function look up tags for a given
    // path and check staleness. Schema fixed by OpenNext's Lambda code, not
    // something we get to choose.
    this.tagCacheTable = new dynamodb.Table(this, 'TagCacheTable', {
      partitionKey: { name: 'tag', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'path', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
    this.tagCacheTable.addGlobalSecondaryIndex({
      indexName: 'revalidate',
      partitionKey: { name: 'path', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'revalidatedAt', type: dynamodb.AttributeType.NUMBER },
    });

    // Revalidation queue: the server function enqueues a message here whenever
    // `revalidateTag`/`revalidatePath` runs; the revalidation Lambda consumes it
    // and issues the actual re-render request. FIFO + content-based dedup avoids
    // redundant re-renders when the same tag is invalidated multiple times.
    this.revalidationQueue = new sqs.Queue(this, 'RevalidationQueue', {
      fifo: true,
      contentBasedDeduplication: true,
      receiveMessageWaitTime: cdk.Duration.seconds(20),
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
