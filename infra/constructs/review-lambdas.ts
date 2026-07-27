import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';
import {
  DOTNET_ARCH,
  DOTNET_MEMORY_MB,
  DOTNET_RUNTIME,
  dotnetLambdaCode,
} from './dotnet-lambda-code';


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

    // One ZIP per service, shared by all its functions; each Lambda selects its
    // handler through ANNOTATIONS_HANDLER instead of a Docker cmd override (ADR-0042).
    const reviewCode = dotnetLambdaCode(
      'src/Services/Review',
      'src/Services/Review/Review.Function/Review.Function.csproj',
    );

    // review-reviews-stream-publisher
    //   Trigger: DynamoDB Streams on reviews (NEW_AND_OLD_IMAGES, CDC — ADR-0005/ADR-0008)
    //   Rule-based dispatcher (ADR-0019): INSERT → ReviewCreatedEvent, MODIFY →
    //   ReviewUpdatedEvent (old + new rating), consumed by CatalogView to keep the
    //   product's AverageRating/RatingCount aggregate (ADR-0029/ADR-0030).
    this.reviewStreamPublisher = new lambda.Function(
      this,
      'ReviewStreamPublisher',
      {
        // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        runtime: DOTNET_RUNTIME,
        // provided.al2023 runs the file named `bootstrap`; this value is inert.
        handler: 'bootstrap',
        code: reviewCode,
        timeout: cdk.Duration.seconds(30),
        memorySize: DOTNET_MEMORY_MB,
        description:
          'CDC: reads DynamoDB Streams on reviews and publishes ReviewCreated/ReviewUpdated to EventBridge',
        environment: {
          ANNOTATIONS_HANDLER: 'ReviewStreamPublisher',
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
