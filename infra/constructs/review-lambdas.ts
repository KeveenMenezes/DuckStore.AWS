import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import { Construct } from 'constructs';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const REVIEW_DOCKERFILE = 'src/Services/Review/Review.Function/Dockerfile';

export interface ReviewLambdasProps {
  readonly reviewsTable: dynamodb.Table;
}

export class ReviewLambdas extends Construct {
  public readonly reviewCreatedPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: ReviewLambdasProps) {
    super(scope, id);

    const { reviewsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

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

    // -------------------------------------------------------------------------
    // review-reviews-event-publisher
    //   Trigger: DynamoDB Streams on reviews (NEW_IMAGE, CDC — ADR-0005/ADR-0008)
    //   On each INSERT, publishes ReviewCreatedEvent to EventBridge so the Catalog
    //   service can update the product's AverageRating/RatingCount.
    // -------------------------------------------------------------------------
    this.reviewCreatedPublisher = new lambda.DockerImageFunction(
      this,
      'ReviewCreatedPublisher',
      {
        functionName: 'review-reviews-event-publisher',
        architecture: DOTNET_ARCH,
        code: lambda.DockerImageCode.fromEcr(reviewImage.repository, {
          tagOrDigest: reviewImage.imageTag,
          cmd: [
            'Review.Function::Review.Function.EventsIntegration.Publisher.ReviewCreatedPublisherFunction::FunctionHandler',
          ],
        }),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'CDC: reads DynamoDB Streams on reviews and publishes ReviewCreatedEvent to EventBridge',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    this.reviewCreatedPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(reviewsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
      }),
    );

    eventBus.grantPutEventsTo(this.reviewCreatedPublisher);
  }
}
