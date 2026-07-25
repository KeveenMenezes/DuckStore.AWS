import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const REVIEW_DOCKERFILE = 'src/Services/Review/Review.Function/Dockerfile';

export interface ReviewLambdasProps {
  readonly reviewsTable: dynamodb.Table;
}

export class ReviewLambdas extends Construct {
  public readonly reviewStreamPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: ReviewLambdasProps) {
    super(scope, id);

    const { reviewsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for async Review processing; a non-empty queue
    // trips the review-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'review' });

    const reviewImage = new ecrAssets.DockerImageAsset(this, 'ReviewImage', {
      directory: REPO_ROOT,
      file: REVIEW_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/Review/Review.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });

    // review-reviews-stream-publisher
    //   Trigger: DynamoDB Streams on reviews (NEW_AND_OLD_IMAGES, CDC — ADR-0005/ADR-0008)
    //   Rule-based dispatcher (ADR-0019): INSERT → ReviewCreatedEvent, MODIFY →
    //   ReviewUpdatedEvent (old + new rating), consumed by CatalogView to keep the
    //   product's AverageRating/RatingCount aggregate (ADR-0029/ADR-0030).
    this.reviewStreamPublisher = new lambda.DockerImageFunction(
      this,
      'ReviewStreamPublisher',
      {
        functionName: 'review-reviews-stream-publisher',
        // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: lambda.DockerImageCode.fromEcr(reviewImage.repository, {
          tagOrDigest: reviewImage.imageTag,
          cmd: [
            'Review.Function::Review.Function.Functions_ReviewStreamPublisher_Generated::ReviewStreamPublisher',
          ],
        }),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'CDC: reads DynamoDB Streams on reviews and publishes ReviewCreated/ReviewUpdated to EventBridge',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    this.reviewStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(reviewsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (shard/sequence
        // metadata, not the payload — redrive by re-reading the stream).
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    eventBus.grantPutEventsTo(this.reviewStreamPublisher);
  }
}
