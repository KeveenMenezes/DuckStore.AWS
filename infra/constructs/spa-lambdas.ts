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
 * Intentionally NOT wired: the warmer function and the dynamodb-provider
 * cache-seed function. Both are pure performance optimizations (avoiding cold
 * starts / pre-populating the ISR cache before first request) — the app is
 * fully correct without them (first requests after deploy just render fresh
 * instead of serving a pre-baked page), and their invocation contracts aren't
 * documented precisely enough to wire with confidence. See ADR-0014.
 */
export class SpaLambdas extends Construct {
  public readonly defaultServerFunction: lambda.Function;
  public readonly defaultServerFunctionUrl: lambda.FunctionUrl;
  public readonly imageOptimizationFunction: lambda.Function;
  public readonly imageOptimizationFunctionUrl: lambda.FunctionUrl;
  public readonly revalidationFunction: lambda.Function;

  constructor(scope: Construct, id: string, props: SpaLambdasProps) {
    super(scope, id);

    const { openNextDir, assetsBucket, tagCacheTable, revalidationQueue, appEnvironment } = props;

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
        REVALIDATION_QUEUE_URL: revalidationQueue.queueUrl,
        REVALIDATION_QUEUE_REGION: cdk.Stack.of(this).region,
        ...appEnvironment,
      },
    });
    assetsBucket.grantReadWrite(this.defaultServerFunction, '_cache/*');
    tagCacheTable.grantReadWriteData(this.defaultServerFunction);
    revalidationQueue.grantSendMessages(this.defaultServerFunction);

    this.defaultServerFunctionUrl = this.defaultServerFunction.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.AWS_IAM,
    });

    // -------------------------------------------------------------------------
    // image-optimization-function — serves `_next/image*`. Reads source images
    // straight from the assets bucket. Next.js images are unoptimized in this
    // app's config, so this path is effectively unused today, but we deploy it
    // to match OpenNext's build output faithfully.
    // -------------------------------------------------------------------------
    this.imageOptimizationFunction = new lambda.Function(this, 'ImageOptimizationFunction', {
      runtime: NODE_RUNTIME,
      architecture: ARCH,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(`${openNextDir}/image-optimization-function`),
      memorySize: 1024,
      timeout: cdk.Duration.seconds(25),
      description: 'OpenNext image optimization function (_next/image)',
      environment: {
        BUCKET_NAME: assetsBucket.bucketName,
        BUCKET_KEY_PREFIX: '_assets',
      },
    });
    assetsBucket.grantRead(this.imageOptimizationFunction, '_assets/*');

    this.imageOptimizationFunctionUrl = this.imageOptimizationFunction.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.AWS_IAM,
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
