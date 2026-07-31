import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';
import {
  DOTNET_ARCH,
  DOTNET_MEMORY_MB,
  DOTNET_RUNTIME,
  dotnetLambdaCode,
} from './dotnet-lambda-code';


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
  public readonly pricingSyncConsumer: lambda.Function;
  public readonly productStreamPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: CatalogViewLambdasProps) {
    super(scope, id);

    const { catalogViewProductsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for all CatalogView consumers; a non-empty
    // queue trips the catalogview-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'catalogview' });

    // One ZIP per service, shared by all its functions; each Lambda selects its
    // handler through ANNOTATIONS_HANDLER instead of a Docker cmd override (ADR-0042).
    const catalogViewCode = dotnetLambdaCode(
      'src/Services/CatalogView',
      'src/Services/CatalogView/CatalogView.Function/CatalogView.Function.csproj',
    );

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
    this.catalogSyncConsumer = new lambda.Function(this, 'CatalogSyncConsumer', {
      functionName: 'catalogview-catalog-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: catalogViewCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'Consumes ProductSyncedEvent/ProductDeletedEvent/CatalogCategorySyncEvent and syncs catalogview-products',
      environment: {
        ANNOTATIONS_HANDLER: 'CatalogSyncConsumer',
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
    this.reviewSyncConsumer = new lambda.Function(this, 'ReviewSyncConsumer', {
      functionName: 'catalogview-review-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: catalogViewCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Consumes ReviewCreatedEvent/ReviewUpdatedEvent and folds ratings into catalogview-products',
      environment: {
        ANNOTATIONS_HANDLER: 'ReviewSyncConsumer',
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

    // 3. catalogview-pricing-sync-consumer
    //    Trigger: every Pricing-sourced occurrence — PriceChangedEvent (the merchant repriced,
    //    ADR-0026/ADR-0028) and ProductDiscountChangedEvent (a campaign started, ended or expired,
    //    ADR-0044). One rule per detail-type, both targeting this one grouped Lambda, dispatched by
    //    IPricingSyncStrategy (ADR-0040 §4). It was a plain 1:1 consumer while Pricing had a single
    //    occurrence; the second one is exactly the trigger ADR-0040's Future Constraints name for
    //    giving a producer its own strategy/dispatcher pair. Both merges are partial and naturally
    //    idempotent (absolute values).
    this.pricingSyncConsumer = new lambda.Function(this, 'PricingSyncConsumer', {
      functionName: 'catalogview-pricing-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: catalogViewCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'Consumes PriceChangedEvent/ProductDiscountChangedEvent and merges price/payment fields into catalogview-products',
      environment: {
        ANNOTATIONS_HANDLER: 'PricingSyncConsumer',
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.pricingSyncConsumer);
    ruleFor(
      'PriceChanged',
      this.pricingSyncConsumer,
      'catalogview-price-changed-rule',
      'PriceChangedEvent',
      'Routes PriceChangedEvent (source=duckstore) to catalogview-pricing-sync-consumer',
    );
    ruleFor(
      'ProductDiscountChanged',
      this.pricingSyncConsumer,
      'catalogview-product-discount-changed-rule',
      'ProductDiscountChangedEvent',
      'Routes ProductDiscountChangedEvent (source=duckstore) to catalogview-pricing-sync-consumer',
    );

    // 4. catalogview-products-stream-publisher (ADR-0035)
    //    Trigger: DynamoDB Streams on catalogview-products, not EventBridge — this is CatalogView's
    //    own CDC publisher, mirroring Pricing's priceStreamPublisher. Emits
    //    CatalogViewProductSyncedEvent (INSERT/MODIFY) / CatalogViewProductDeletedEvent (REMOVE)
    //    only after a CatalogView write commits, so the SPA revalidator (subscribed to these events
    //    instead of the upstream Catalog/Pricing/Review ones) can never invalidate CloudFront before
    //    CatalogView's own data is in place.
    this.productStreamPublisher = new lambda.Function(this, 'ProductStreamPublisher', {
      functionName: 'catalogview-products-stream-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: catalogViewCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'Publishes CatalogViewProductSyncedEvent/CatalogViewProductDeletedEvent off catalogview-products writes',
      environment: {
        ANNOTATIONS_HANDLER: 'CatalogViewProductStreamPublisher',
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
