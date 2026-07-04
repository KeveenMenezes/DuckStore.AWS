import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';

const NODE_RUNTIME = lambda.Runtime.NODEJS_22_X;
const ARCH = lambda.Architecture.ARM_64;

export interface SpaLambdasProps {
  /** Path to the SPA's `.open-next` build output. */
  readonly openNextDir: string;
  readonly assetsBucket: s3.Bucket;
  readonly tagCacheTable: dynamodb.Table;
  readonly revalidationQueue: sqs.Queue;
  /**
   * Shared secret CloudFront injects as a custom origin header on every
   * request to the server function; middleware.ts rejects requests without
   * a matching x-origin-verify header. See spa-stack.ts for why this
   * replaces AWS_IAM + OAC on the Function URL.
   */
  readonly originVerifySecret: string;
  /**
   * Server-only app env vars (AppSync URL/key, Cognito, webhook secrets, etc.)
   * forwarded to the `default` server function alongside OpenNext's own
   * cache/queue wiring.
   */
  readonly appEnvironment: Record<string, string>;
}

/**
 * The three OpenNext Lambda functions we deploy (per `open-next.output.json`):
 * the `default` server function (SSR/API routes), the image optimizer, and the
 * revalidation function (SQS-triggered).
 *
 * Intentionally NOT wired: the warmer function (a pure performance
 * optimization — avoiding cold starts — the app is fully correct without it).
 * The dynamodb-provider cache-seed function IS required, not optional: it
 * populates the tag-cache table's tag -> path rows for every prerendered ISR
 * page's tagged fetch() calls, and without it `getByTag()`/`getByPath()`
 * (used by `revalidateTag()` and by SpaTagRevalidator) have nothing to look
 * up, silently no-op'ing all on-demand revalidation forever. Its own
 * Lambda's event contract isn't documented precisely enough to wire with
 * confidence, so `SpaTagCacheSeeder` (spa-tag-cache-seeder.ts) replicates it
 * with plain `AwsCustomResource` `batchWriteItem` calls instead. See
 * ADR-0014.
 */
export class SpaLambdas extends Construct {
  public readonly defaultServerFunction: lambda.Function;
  public readonly defaultServerFunctionUrl: lambda.FunctionUrl;
  public readonly imageOptimizationFunction: lambda.Function;
  public readonly imageOptimizationFunctionUrl: lambda.FunctionUrl;
  public readonly revalidationFunction: lambda.Function;

  constructor(scope: Construct, id: string, props: SpaLambdasProps) {
    super(scope, id);

    const { openNextDir, assetsBucket, tagCacheTable, revalidationQueue, originVerifySecret, appEnvironment } = props;

    // OpenNext's cache handler prefixes every DynamoDB tag-cache key with this
    // build ID ("{buildId}/products", not "products") so a new deploy's cache
    // can't collide with the previous build's — see the bundled cache.cjs
    // (`{OPEN_NEXT_BUILD_ID: $v} = process.env`). Must match the prefix baked
    // into dynamodb-cache.json by SpaTagCacheSeeder and read by
    // SpaTagRevalidator, or tag lookups/writes silently match nothing.
    const buildId = fs.readFileSync(path.join(openNextDir, 'assets', 'BUILD_ID'), 'utf8').trim();

    // -------------------------------------------------------------------------
    // default — SSR/ISR/API routes. Handles every request except static assets
    // and image optimization (see CloudFront behaviors in spa-distribution.ts).
    // -------------------------------------------------------------------------
    this.defaultServerFunction = new lambda.Function(this, 'DefaultServerFunction', {
      runtime: NODE_RUNTIME,
      architecture: ARCH,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(`${openNextDir}/server-functions/default`),
      memorySize: 1024,
      timeout: cdk.Duration.seconds(30),
      description: 'OpenNext server function — Next.js SSR/ISR/API routes',
      environment: {
        CACHE_BUCKET_NAME: assetsBucket.bucketName,
        CACHE_BUCKET_REGION: cdk.Stack.of(this).region,
        CACHE_BUCKET_KEY_PREFIX: '_cache',
        CACHE_DYNAMO_TABLE: tagCacheTable.tableName,
        OPEN_NEXT_BUILD_ID: buildId,
        REVALIDATION_QUEUE_URL: revalidationQueue.queueUrl,
        REVALIDATION_QUEUE_REGION: cdk.Stack.of(this).region,
        ORIGIN_VERIFY_SECRET: originVerifySecret,
        ...appEnvironment,
      },
    });
    assetsBucket.grantReadWrite(this.defaultServerFunction, '_cache/*');
    tagCacheTable.grantReadWriteData(this.defaultServerFunction);
    revalidationQueue.grantSendMessages(this.defaultServerFunction);

    // NONE (public), not AWS_IAM+OAC — see originVerifySecret doc comment
    // above for why. middleware.ts is the actual access gate.
    this.defaultServerFunctionUrl = this.defaultServerFunction.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // image-optimization-function — serves `_next/image*`. Resizes/re-encodes
    // source images (local /public assets from the assets bucket, and product
    // images fetched over HTTPS) to AVIF/WebP via sharp. open-next.config.ts
    // installs a linux-arm64 sharp so this actually runs on the Lambda.
    // -------------------------------------------------------------------------
    this.imageOptimizationFunction = new lambda.Function(this, 'ImageOptimizationFunction', {
      runtime: NODE_RUNTIME,
      architecture: ARCH,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(`${openNextDir}/image-optimization-function`),
      memorySize: 1536,
      timeout: cdk.Duration.seconds(25),
      description: 'OpenNext image optimization function (_next/image)',
      environment: {
        BUCKET_NAME: assetsBucket.bucketName,
        BUCKET_KEY_PREFIX: '_assets',
      },
    });
    assetsBucket.grantRead(this.imageOptimizationFunction, '_assets/*');

    // Public (NONE): only resizes/serves already-public images from the
    // assets bucket, no sensitive data — matches OpenNext's own reference
    // architecture. No origin-verify header check needed here.
    this.imageOptimizationFunctionUrl = this.imageOptimizationFunction.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // revalidation-function — consumes the SQS queue and issues the actual
    // re-render request for a stale path. The message already carries the
    // target host/url, so no extra environment wiring is needed here.
    // -------------------------------------------------------------------------
    this.revalidationFunction = new lambda.Function(this, 'RevalidationFunction', {
      runtime: NODE_RUNTIME,
      architecture: ARCH,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(`${openNextDir}/revalidation-function`),
      memorySize: 256,
      timeout: cdk.Duration.seconds(30),
      description: 'OpenNext revalidation function — consumes the ISR revalidation SQS queue',
    });
    this.revalidationFunction.addEventSource(
      new lambdaEventSources.SqsEventSource(revalidationQueue, { batchSize: 5 }),
    );
  }
}
