import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import { Construct } from 'constructs';

const DOTNET_RUNTIME = lambda.Runtime.DOTNET_10;
const DOTNET_ARCH = lambda.Architecture.ARM_64;

// Populate before `cdk deploy` by running:
//   dotnet publish ../src/Services/Catalog/Catalog.Function \
//     -c Release -r linux-arm64 --self-contained false \
//     -o infra/publish/catalog
const CATALOG_PUBLISH_PATH = path.join(__dirname, '..', 'publish', 'catalog');

const HANDLER_PREFIX =
  'Catalog.Function::Catalog.Function.Modules.Products.EventsIntegration.';

export interface CatalogLambdasProps {
  readonly productsTable: dynamodb.Table;
  readonly categoriesTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
  /** Base URL of the Next.js SPA. Consumer appends /api/webhooks/catalog-updated. */
  readonly spaWebhookUrl: string;
  readonly catalogWebhookSecret: string;
}

export class CatalogLambdas extends Construct {
  public readonly eventBus: events.EventBus;
  public readonly streamPublisher: lambda.Function;
  public readonly catalogUpdatedConsumer: lambda.Function;
  public readonly reviewCreatedConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: CatalogLambdasProps) {
    super(scope, id);

    const { productsTable, processedEventsTable, spaWebhookUrl, catalogWebhookSecret } =
      props;

    // All Catalog integration events flow through this bus (ADR-0004).
    this.eventBus = new events.EventBus(this, 'EventBus', {
      eventBusName: 'duckstore-event-bus',
    });

    // All three Lambdas share the same Catalog.Function assembly.
    const code = lambda.Code.fromAsset(CATALOG_PUBLISH_PATH);

    // -------------------------------------------------------------------------
    // 1. catalog-stream-event-publisher
    //    Trigger: DynamoDB Streams on products
    //    IAM: DynamoEventSource grants stream read; grantPutEventsTo for EventBridge
    // -------------------------------------------------------------------------
    this.streamPublisher = new lambda.Function(this, 'StreamPublisher', {
      functionName: 'catalog-stream-event-publisher',
      runtime: DOTNET_RUNTIME,
      architecture: DOTNET_ARCH,
      // Full path exceeds Lambda's 128-char limit; relay class at Handlers.CatalogStreamPublisher.
      handler: 'Catalog.Function::Catalog.Function.Handlers.CatalogStreamPublisher::FunctionHandler',
      code,
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'CDC: reads DynamoDB Streams on products and publishes CatalogUpdatedEvent to EventBridge',
      environment: {
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
      }),
    );

    this.eventBus.grantPutEventsTo(this.streamPublisher);

    // -------------------------------------------------------------------------
    // 2. catalog-catalog-updated-consumer
    //    Trigger: EventBridge rule (CatalogUpdatedEvent)
    //    IAM: none — only makes outbound HTTPS calls to the SPA webhook
    // -------------------------------------------------------------------------
    this.catalogUpdatedConsumer = new lambda.Function(this, 'CatalogUpdatedConsumer', {
      functionName: 'catalog-catalog-updated-consumer',
      runtime: DOTNET_RUNTIME,
      architecture: DOTNET_ARCH,
      handler: `${HANDLER_PREFIX}Consumer.CatalogUpdatedConsumerFunction::FunctionHandler`,
      code,
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'Consumes CatalogUpdatedEvent and POSTs to the Next.js ISR webhook to trigger revalidateTag',
      environment: {
        Catalog__WebhookUrl: `${spaWebhookUrl}/api/webhooks/catalog-updated`,
        CATALOG_WEBHOOK_SECRET: catalogWebhookSecret,
      },
    });

    const catalogUpdatedRule = new events.Rule(this, 'CatalogUpdatedRule', {
      eventBus: this.eventBus,
      ruleName: 'catalog-updated-consumer-rule',
      description:
        'Routes CatalogUpdatedEvent (source=duckstore) to catalog-catalog-updated-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['CatalogUpdatedEvent'],
      },
    });
    catalogUpdatedRule.addTarget(
      new targets.LambdaFunction(this.catalogUpdatedConsumer),
    );

    // -------------------------------------------------------------------------
    // 3. catalog-review-created-consumer
    //    Trigger: EventBridge rule (ReviewCreatedEvent from Review service)
    //    IAM: read+write on products and catalog-processed-events (ADR-0011)
    // -------------------------------------------------------------------------
    this.reviewCreatedConsumer = new lambda.Function(this, 'ReviewCreatedConsumer', {
      functionName: 'catalog-review-created-consumer',
      runtime: DOTNET_RUNTIME,
      architecture: DOTNET_ARCH,
      handler: `${HANDLER_PREFIX}Consumer.ReviewCreatedConsumerFunction::FunctionHandler`,
      code,
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'Consumes ReviewCreatedEvent and updates product RatingCount/RatingSum/AverageRating idempotently (ADR-0011)',
      environment: {
        EventBridge__BusName: this.eventBus.eventBusName,
      },
    });

    // Covers GetItem (average recompute) + UpdateItem / ConditionCheckItem
    // used inside TransactWriteItems for rating aggregation.
    productsTable.grantReadWriteData(this.reviewCreatedConsumer);
    // Covers PutItem + ConditionCheckItem for the idempotency inbox leg of TransactWriteItems.
    processedEventsTable.grantReadWriteData(this.reviewCreatedConsumer);

    const reviewCreatedRule = new events.Rule(this, 'ReviewCreatedRule', {
      eventBus: this.eventBus,
      ruleName: 'review-created-consumer-rule',
      description:
        'Routes ReviewCreatedEvent (source=duckstore) to catalog-review-created-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['ReviewCreatedEvent'],
      },
    });
    reviewCreatedRule.addTarget(
      new targets.LambdaFunction(this.reviewCreatedConsumer),
    );
  }
}
