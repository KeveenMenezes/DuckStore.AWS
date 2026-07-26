import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const CATALOGVIEW_DOCKERFILE = 'src/Services/CatalogView/CatalogView.Function/Dockerfile';

export interface CatalogViewLambdasProps {
  readonly catalogViewProductsTable: dynamodb.Table;
}

// Three EventBridge-triggered consumer Lambdas — none HTTP/AppSync-invoked (ADR-0030 reverted
// products/product back to Direct DynamoDB resolvers, so CatalogView no longer exposes any
// synchronously invoked Lambda). Each keeps `catalogview-products` in sync with a slice of another
// bounded context's writes via CDC. Grouped by producer bounded context (ADR-0040): Catalog and
// Review each have 2+ occurrences relevant here, so they share one Lambda dispatching to an
// ISyncStrategy per detail-type; Pricing has a single occurrence and stays a plain 1:1 consumer.
export class CatalogViewLambdas extends Construct {
  public readonly catalogSyncConsumer: lambda.Function;
  public readonly reviewSyncConsumer: lambda.Function;
  public readonly priceSyncConsumer: lambda.Function;
  public readonly productStreamPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: CatalogViewLambdasProps) {
    super(scope, id);

    const { catalogViewProductsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for all CatalogView consumers; a non-empty
    // queue trips the catalogview-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'catalogview' });

    // All CatalogView Lambdas share the same image, built once.
    const catalogViewImage = new ecrAssets.DockerImageAsset(this, 'CatalogViewImage', {
      directory: REPO_ROOT,
      file: CATALOGVIEW_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/CatalogView/CatalogView.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const catalogViewCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(catalogViewImage.repository, {
        tagOrDigest: catalogViewImage.imageTag,
        cmd,
      });

    // A grouped consumer (ADR-0040) is passed to ruleFor once per detail-type it
    // handles, but CDK only allows configureAsyncInvoke to be called once per
    // function — track which functions have already been configured.
    const asyncInvokeConfigured = new Set<lambda.Function>();

    const ruleFor = (
      idPrefix: string,
      fn: lambda.Function,
      ruleName: string,
      detailType: string,
      description: string,
    ) => {
      const rule = new events.Rule(this, `${idPrefix}Rule`, {
        eventBus,
        ruleName,
        description,
        eventPattern: {
          source: ['duckstore'],
          detailType: [detailType],
        },
      });
      // Two failure paths, one queue: the async-invoke destination captures the
      // event when the Lambda keeps throwing; the rule-target DLQ captures events
      // EventBridge could not deliver to the Lambda at all.
      if (!asyncInvokeConfigured.has(fn)) {
        fn.configureAsyncInvoke({
          onFailure: new destinations.SqsDestination(dlq.queue),
          retryAttempts: 2,
        });
        asyncInvokeConfigured.add(fn);
      }
      rule.addTarget(
        new targets.LambdaFunction(fn, {
          deadLetterQueue: dlq.queue,
          retryAttempts: 3,
          maxEventAge: cdk.Duration.hours(2),
        }),
      );
    };

    // 1. catalogview-catalog-sync-consumer (ADR-0040)
    //    Triggers: ProductSyncedEvent, ProductDeletedEvent, CatalogCategorySyncEvent — every
    //    CatalogView consumer sourced from Catalog, grouped behind one Lambda that dispatches to
    //    the owning ISyncStrategy by detail-type. Each strategy keeps its own narrow write
    //    boundary via IProductSearchIndex — ProductSyncStrategy never touches
    //    Price/AverageRating/RatingCount/RatingSum/LastRatingEventId (ADR-0027 §3, ADR-0030).
    this.catalogSyncConsumer = new lambda.DockerImageFunction(this, 'CatalogSyncConsumer', {
      functionName: 'catalogview-catalog-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_CatalogSyncConsumer_Generated::CatalogSyncConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'Consumes ProductSyncedEvent/ProductDeletedEvent/CatalogCategorySyncEvent and syncs catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.catalogSyncConsumer);
    ruleFor(
      'ProductSync',
      this.catalogSyncConsumer,
      'catalogview-product-synced-rule',
      'ProductSyncedEvent',
      'Routes ProductSyncedEvent (source=duckstore) to catalogview-catalog-sync-consumer',
    );
    ruleFor(
      'ProductDeleted',
      this.catalogSyncConsumer,
      'catalogview-product-deleted-rule',
      'ProductDeletedEvent',
      'Routes ProductDeletedEvent (source=duckstore) to catalogview-catalog-sync-consumer',
    );
    ruleFor(
      'CategorySync',
      this.catalogSyncConsumer,
      'catalogview-category-synced-rule',
      'CatalogCategorySyncEvent',
      'Routes CatalogCategorySyncEvent (source=duckstore) to catalogview-catalog-sync-consumer',
    );

    // 2. catalogview-review-sync-consumer (ADR-0040)
    //    Triggers: ReviewCreatedEvent, ReviewUpdatedEvent — every CatalogView consumer sourced
    //    from Review, grouped behind one Lambda. Both strategies apply the same two-step,
    //    non-atomic ADD + recompute average (ADR-0030 accepted trade-off 2); ReviewUpdateStrategy
    //    is the sibling that moves ratingSum by delta instead of ratingCount+1.
    this.reviewSyncConsumer = new lambda.DockerImageFunction(this, 'ReviewSyncConsumer', {
      functionName: 'catalogview-review-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_ReviewSyncConsumer_Generated::ReviewSyncConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Consumes ReviewCreatedEvent/ReviewUpdatedEvent and folds ratings into catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.reviewSyncConsumer);
    ruleFor(
      'ReviewAggregate',
      this.reviewSyncConsumer,
      'catalogview-review-created-rule',
      'ReviewCreatedEvent',
      'Routes ReviewCreatedEvent (source=duckstore) to catalogview-review-sync-consumer',
    );
    ruleFor(
      'ReviewUpdateAggregate',
      this.reviewSyncConsumer,
      'catalogview-review-updated-rule',
      'ReviewUpdatedEvent',
      'Routes ReviewUpdatedEvent (source=duckstore) to catalogview-review-sync-consumer',
    );

    // 3. catalogview-price-sync-consumer
    //    Trigger: PriceChangedEvent (Pricing's CDC stream publisher, ADR-0026/ADR-0028). Pricing
    //    is the only producer with a single occurrence relevant to CatalogView, so this stays a
    //    plain 1:1 consumer (ADR-0040 §1) — partial merge of price + payment-highlight fields,
    //    naturally idempotent (absolute values).
    this.priceSyncConsumer = new lambda.DockerImageFunction(this, 'PriceSyncConsumer', {
      functionName: 'catalogview-price-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_PriceSyncConsumer_Generated::PriceSyncConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Consumes PriceChangedEvent and merges price/payment fields into catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.priceSyncConsumer);
    ruleFor(
      'PriceSync',
      this.priceSyncConsumer,
      'catalogview-price-sync-consumer-rule',
      'PriceChangedEvent',
      'Routes PriceChangedEvent (source=duckstore) to catalogview-price-sync-consumer',
    );

    // 4. catalogview-products-stream-publisher (ADR-0035)
    //    Trigger: DynamoDB Streams on catalogview-products, not EventBridge — this is CatalogView's
    //    own CDC publisher, mirroring Pricing's priceStreamPublisher. Emits
    //    CatalogViewProductSyncedEvent (INSERT/MODIFY) / CatalogViewProductDeletedEvent (REMOVE)
    //    only after a CatalogView write commits, so the SPA revalidator (subscribed to these events
    //    instead of the upstream Catalog/Pricing/Review ones) can never invalidate CloudFront before
    //    CatalogView's own data is in place.
    this.productStreamPublisher = new lambda.DockerImageFunction(this, 'ProductStreamPublisher', {
      functionName: 'catalogview-products-stream-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_CatalogViewProductStreamPublisher_Generated::CatalogViewProductStreamPublisher',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'Publishes CatalogViewProductSyncedEvent/CatalogViewProductDeletedEvent off catalogview-products writes',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });

    this.productStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(catalogViewProductsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    eventBus.grantPutEventsTo(this.productStreamPublisher);

    // Note: products/product are AppSync direct DynamoDB resolvers against catalogview-products
    // (ADR-0030, reverses ADR-0027 §5) — not Lambdas — see infra/constructs/appsync-api.ts.
  }
}
