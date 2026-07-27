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



export interface CatalogLambdasProps {
  readonly productsTable: dynamodb.Table;
  readonly categoriesTable: dynamodb.Table;
}

export class CatalogLambdas extends Construct {
  public readonly eventBus: events.IEventBus;
  public readonly streamPublisher: lambda.Function;
  public readonly categoryStreamPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: CatalogLambdasProps) {
    super(scope, id);

    const { productsTable, categoriesTable } = props;

    // Shared dead-letter queue for every async Catalog process; a non-empty
    // queue trips the catalog-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'catalog' });

    // All Catalog integration events flow through this bus (ADR-0004). Owned by
    // FoundationStack, not Catalog — a resource shared by 7 stacks can't live inside
    // one of its consumers (see infra/stacks/foundation-stack.ts). Imported by fixed
    // name like every other consumer; FoundationStack just has to deploy first.
    this.eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // One ZIP per service, shared by all its functions; each Lambda selects its
    // handler through ANNOTATIONS_HANDLER instead of a Docker cmd override (ADR-0042).
    const catalogCode = dotnetLambdaCode(
      'src/Services/Catalog',
      'src/Services/Catalog/Catalog.Function/Catalog.Function.csproj',
    );

    // 1. catalog-products-stream-publisher
    //    Trigger: DynamoDB Streams on products
    //    IAM: DynamoEventSource grants stream read; grantPutEventsTo for EventBridge
    this.streamPublisher = new lambda.Function(this, 'StreamPublisher', {
      functionName: 'catalog-products-stream-publisher',
      // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: catalogCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'CDC: reads DynamoDB Streams on products and publishes ProductCreatedEvent/ProductUpdatedEvent/ProductDeletedEvent/ProductSyncedEvent to EventBridge (ADR-0031)',
      environment: {
        ANNOTATIONS_HANDLER: 'ProductStreamPublisher',
        EventBridge__BusName: this.eventBus.eventBusName,
      },
    });

    // DynamoEventSource creates the event source mapping and calls
    // productsTable.grantStreamRead(this.streamPublisher) automatically.
    this.streamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(productsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (shard/sequence
        // metadata, not the payload — redrive by re-reading the stream).
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    this.eventBus.grantPutEventsTo(this.streamPublisher);

    categoriesTable.grantReadData(this.streamPublisher);

    // ProductCreatedEvent/ProductUpdatedEvent/ProductDeletedEvent's ISR revalidation trigger is
    // an SST-managed Lambda (revalidator/index.mjs, see the SPA's sst.config.ts) subscribed to
    // this same bus directly — no HTTP webhook needed.

    // 1b. catalog-categories-stream-publisher
    //    Trigger: DynamoDB Streams on categories
    //    Fires only on a rename (CatalogCategorySyncRule) — publishes
    //    CatalogCategorySyncEvent so CatalogView can rewrite the denormalized
    //    category name on every product document that references it.
    this.categoryStreamPublisher = new lambda.Function(this, 'CategoryStreamPublisher', {
      functionName: 'catalog-categories-stream-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: catalogCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'CDC: reads DynamoDB Streams on categories and publishes CatalogCategorySyncEvent on rename',
      environment: {
        ANNOTATIONS_HANDLER: 'CategoryStreamPublisher',
        EventBridge__BusName: this.eventBus.eventBusName,
      },
    });

    this.categoryStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(categoriesTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    this.eventBus.grantPutEventsTo(this.categoryStreamPublisher);

    // The old catalog-review-created-consumer (ADR-0011 §4) is gone: rating aggregation is
    // owned by CatalogView's catalogview-review-sync-consumer (ADR-0027/ADR-0030/ADR-0040), so
    // ReviewCreatedEvent no longer touches the products table.
  }
}
