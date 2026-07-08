import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import { Construct } from 'constructs';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

// Build context is the repo root: Catalog.Function's Dockerfile needs
// Directory.Packages.props/nuget.config and the BuildingBlocks project
// references, which all live outside the Catalog.Function folder.
const REPO_ROOT = path.join(__dirname, '..', '..');
const CATALOG_DOCKERFILE = 'src/Services/Catalog/Catalog.Function/Dockerfile';


export interface CatalogLambdasProps {
  readonly productsTable: dynamodb.Table;
  readonly categoriesTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
}

export class CatalogLambdas extends Construct {
  public readonly eventBus: events.EventBus;
  public readonly streamPublisher: lambda.Function;
  public readonly categoryStreamPublisher: lambda.Function;
  public readonly reviewCreatedConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: CatalogLambdasProps) {
    super(scope, id);

    const { productsTable, categoriesTable, processedEventsTable } = props;

    // All Catalog integration events flow through this bus (ADR-0004).
    this.eventBus = new events.EventBus(this, 'EventBus', {
      eventBusName: 'duckstore-event-bus',
    });

    // All three Lambdas share the same Catalog.Function image, built once and
    // referenced per-function with a different handler via the `cmd` override.
    const catalogImage = new ecrAssets.DockerImageAsset(this, 'CatalogImage', {
      directory: REPO_ROOT,
      file: CATALOG_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      // Scope the build context down to what the Dockerfile actually COPYs —
      // without this, staging tries to copy the whole repo (.git, cdk.out, etc).
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/Catalog/Catalog.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const catalogCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(catalogImage.repository, {
        tagOrDigest: catalogImage.imageTag,
        cmd,
      });

    // -------------------------------------------------------------------------
    // 1. catalog-stream-event-publisher
    //    Trigger: DynamoDB Streams on products
    //    IAM: DynamoEventSource grants stream read; grantPutEventsTo for EventBridge
    // -------------------------------------------------------------------------
    this.streamPublisher = new lambda.DockerImageFunction(this, 'StreamPublisher', {
      functionName: 'catalog-stream-event-publisher',
      // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogCode([
        'Catalog.Function::Catalog.Function.Functions_ProductStreamPublisher_Generated::ProductStreamPublisher',
      ]),
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

    // CatalogUpdatedEvent's ISR revalidation trigger moved to
    // SpaTagRevalidator (infra/constructs/spa-tag-revalidator.ts), which
    // subscribes to this same bus directly — no HTTP webhook needed.

    // -------------------------------------------------------------------------
    // 1b. catalog-category-stream-publisher
    //    Trigger: DynamoDB Streams on categories
    //    Fires only on a rename (CatalogCategorySyncRule) — publishes
    //    CatalogCategorySyncEvent so CatalogView can rewrite the denormalized
    //    category name on every product document that references it.
    // -------------------------------------------------------------------------
    this.categoryStreamPublisher = new lambda.DockerImageFunction(this, 'CategoryStreamPublisher', {
      functionName: 'catalog-category-stream-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogCode([
        'Catalog.Function::Catalog.Function.Functions_CategoryStreamPublisher_Generated::CategoryStreamPublisher',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'CDC: reads DynamoDB Streams on categories and publishes CatalogCategorySyncEvent on rename',
      environment: {
        EventBridge__BusName: this.eventBus.eventBusName,
      },
    });

    this.categoryStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(categoriesTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
      }),
    );

    this.eventBus.grantPutEventsTo(this.categoryStreamPublisher);

    // -------------------------------------------------------------------------
    // 2. catalog-review-created-consumer
    //    Trigger: EventBridge rule (ReviewCreatedEvent from Review service)
    //    IAM: read+write on products and catalog-processed-events (ADR-0011)
    // -------------------------------------------------------------------------
    this.reviewCreatedConsumer = new lambda.DockerImageFunction(this, 'ReviewCreatedConsumer', {
      functionName: 'catalog-review-created-consumer',
      // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogCode([
        'Catalog.Function::Catalog.Function.Functions_ReviewCreatedConsumer_Generated::ReviewCreatedConsumer',
      ]),
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
