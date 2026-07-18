import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
import * as cloudwatchActions from 'aws-cdk-lib/aws-cloudwatch-actions';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { SqsEventSource } from 'aws-cdk-lib/aws-lambda-event-sources';
import { NodejsFunction } from 'aws-cdk-lib/aws-lambda-nodejs';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as targets from 'aws-cdk-lib/aws-route53-targets';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3n from 'aws-cdk-lib/aws-s3-notifications';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';
import { importAlertsTopic } from '../constructs/context-dlq';

export interface ProductImagesStackProps extends cdk.StackProps {
  /** e.g. dev-img-duckstore.keveenmenezes.com — the image CDN domain clients build URLs on. */
  readonly imageDomainName: string;
  /** Route53 public hosted zone the CDN record is created in (keveenmenezes.com). */
  readonly hostedZoneDomainName: string;
  /** Browser origins allowed to POST uploads (Blazor admin dev server + deployed admin site). */
  readonly uploadOrigins: string[];
}

const PRODUCT_IMAGES_DIR = path.join(__dirname, '../../src/Services/ProductImages');

/**
 * Product image pipeline (ADR-0034): originals bucket (browser presigned-POST uploads) →
 * SQS work queue → sharp processor Lambda → processed bucket served by CloudFront (OAC)
 * with immutable objects. First Node.js Lambdas in this app — every other Lambda is .NET
 * on a Docker image; sharp is the reason (no mature AVIF encoder in .NET).
 */
export class ProductImagesStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: ProductImagesStackProps) {
    super(scope, id, props);

    const originalsBucket = new s3.Bucket(this, 'OriginalsBucket', {
      bucketName: `duckstore-product-images-original-${this.account}`,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      // Fixed bucketName means a failed stack create/update can't just retry with a fresh
      // name — CDK's L2 default (RETAIN) would orphan this bucket on rollback and collide
      // with the next attempt. DESTROY + autoDeleteObjects makes a failed deploy self-heal
      // (matches ManagementStack's site bucket); acceptable for a study project's demo data.
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
      // The browser POSTs the presigned form cross-origin; without this rule the
      // preflight fails and every upload dies as a generic "Failed to fetch".
      cors: [
        {
          allowedOrigins: props.uploadOrigins,
          allowedMethods: [s3.HttpMethods.POST],
          allowedHeaders: ['*'],
        },
      ],
      lifecycleRules: [
        {
          // Originals are rarely read after processing — IA cuts their storage cost.
          // Never expired: age cannot distinguish an orphan from a live product's
          // original (orphans are removed by scripts/sweep-orphan-images.ts instead).
          prefix: 'images/',
          transitions: [
            {
              storageClass: s3.StorageClass.INFREQUENT_ACCESS,
              transitionAfter: cdk.Duration.days(30),
            },
          ],
        },
        { prefix: 'quarantine/', expiration: cdk.Duration.days(30) },
        { abortIncompleteMultipartUploadAfter: cdk.Duration.days(1) },
      ],
    });

    const processedBucket = new s3.Bucket(this, 'ProcessedBucket', {
      bucketName: `duckstore-product-images-${this.account}`,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    // --- Processing pipeline: S3 → SQS (+DLQ) → sharp Lambda -------------------------

    const deadLetterQueue = new sqs.Queue(this, 'ProcessingDlq', {
      queueName: 'product-images-dlq',
      retentionPeriod: cdk.Duration.days(14),
    });

    const queue = new sqs.Queue(this, 'ProcessingQueue', {
      queueName: 'product-images-queue',
      // AWS guidance for Lambda consumers: at least 6x the function timeout.
      visibilityTimeout: cdk.Duration.seconds(360),
      deadLetterQueue: { queue: deadLetterQueue, maxReceiveCount: 4 },
    });

    // A message in the DLQ is an image that silently failed processing — its product
    // renders a placeholder forever unless someone acts on this alarm.
    const processingDlqAlarm = new cloudwatch.Alarm(this, 'ProcessingDlqAlarm', {
      alarmName: 'product-images-dlq-not-empty',
      metric: deadLetterQueue.metricApproximateNumberOfMessagesVisible(),
      threshold: 0,
      comparisonOperator: cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      evaluationPeriods: 1,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });
    processingDlqAlarm.addAlarmAction(new cloudwatchActions.SnsAction(importAlertsTopic(this)));

    // The images/ prefix is load-bearing: the processor writes quarantined files to
    // quarantine/ in this same bucket — without the filter that write would loop.
    originalsBucket.addEventNotification(
      s3.EventType.OBJECT_CREATED,
      new s3n.SqsDestination(queue),
      { prefix: 'images/' },
    );

    const processor = new NodejsFunction(this, 'ProcessorFunction', {
      functionName: 'product-images-processor',
      entry: path.join(PRODUCT_IMAGES_DIR, 'processor/handler.ts'),
      depsLockFilePath: path.join(PRODUCT_IMAGES_DIR, 'package-lock.json'),
      runtime: lambda.Runtime.NODEJS_22_X,
      architecture: lambda.Architecture.ARM_64,
      // Memory is the CPU knob on Lambda; 1024 is the starting point — tune with the
      // Lambda Power Tuning state machine once real originals flow (ADR-0034).
      memorySize: 1024,
      timeout: cdk.Duration.seconds(60),
      // No reservedConcurrentExecutions: this dev account's unreserved pool has no headroom
      // to carve a slice out of (AWS enforces a 10-execution floor account-wide, and
      // reserving here pushed it below that). SQS's own backpressure is enough throttling
      // for a bulk import at this project's scale; revisit if the account's limit grows.
      environment: { PROCESSED_BUCKET: processedBucket.bucketName },
      bundling: {
        nodeModules: ['sharp'],
        forceDockerBundling: true,
        commandHooks: {
          beforeBundling: () => [],
          beforeInstall: () => [],
          afterBundling: (_inputDir: string, outputDir: string) => [
            `cd ${outputDir}`,
            'npm install --no-save --os=linux --cpu=arm64 sharp',
          ],
        },
      },
    });

    processor.addEventSource(
      new SqsEventSource(queue, { batchSize: 5, reportBatchItemFailures: true }),
    );

    originalsBucket.grantRead(processor);
    // Quarantine flow: CopyObject to quarantine/ + DeleteObject of the original.
    originalsBucket.grantPut(processor, 'quarantine/*');
    originalsBucket.grantDelete(processor, 'images/*');
    processedBucket.grantPut(processor, 'images/*');

    // --- Presign Lambda (AppSync resolver datasource, imported by name) ---------------

    const presign = new NodejsFunction(this, 'PresignFunction', {
      // Fixed name — appsync-api.ts imports it via Function.fromFunctionName, the same
      // no-CloudFormation-coupling convention as every other Lambda datasource.
      functionName: 'product-images-presign',
      entry: path.join(PRODUCT_IMAGES_DIR, 'presign/handler.ts'),
      depsLockFilePath: path.join(PRODUCT_IMAGES_DIR, 'package-lock.json'),
      runtime: lambda.Runtime.NODEJS_22_X,
      architecture: lambda.Architecture.ARM_64,
      memorySize: 256,
      timeout: cdk.Duration.seconds(10),
      environment: { ORIGINALS_BUCKET: originalsBucket.bucketName },
    });

    // The presigned POST is signed with this role's credentials — the browser's upload
    // is only as capable as this grant.
    originalsBucket.grantPut(presign, 'images/*');

    // --- CDN: CloudFront + OAC over the processed bucket ------------------------------

    const zone = route53.HostedZone.fromLookup(this, 'Zone', {
      domainName: props.hostedZoneDomainName,
    });

    const certificate = new acm.Certificate(this, 'ImageCdnCertificate', {
      domainName: props.imageDomainName,
      validation: acm.CertificateValidation.fromDns(zone),
    });

    const distribution = new cloudfront.Distribution(this, 'ImageCdnDistribution', {
      comment: 'DuckStore product images (processed variants, immutable objects)',
      domainNames: [props.imageDomainName],
      certificate,
      defaultBehavior: {
        origin: origins.S3BucketOrigin.withOriginAccessControl(processedBucket),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
      },
      // A product page visited between saga commit and the variants landing hits
      // 403/404 — CloudFront's default error caching (5 min) would pin that miss long
      // after the object exists. Objects themselves are immutable, so only errors
      // need a short TTL.
      errorResponses: [
        { httpStatus: 403, ttl: cdk.Duration.seconds(5) },
        { httpStatus: 404, ttl: cdk.Duration.seconds(5) },
      ],
    });

    new route53.ARecord(this, 'ImageCdnAliasRecord', {
      zone,
      recordName: props.imageDomainName.replace(`.${props.hostedZoneDomainName}`, ''),
      target: route53.RecordTarget.fromAlias(new targets.CloudFrontTarget(distribution)),
    });

    new cdk.CfnOutput(this, 'ImageCdnUrl', {
      value: `https://${props.imageDomainName}`,
      description: 'Base URL clients prepend to images/{imageId}/{size}.{fmt}',
    });

    new cdk.CfnOutput(this, 'OriginalsBucketName', { value: originalsBucket.bucketName });
    new cdk.CfnOutput(this, 'ProcessedBucketName', { value: processedBucket.bucketName });
  }
}
