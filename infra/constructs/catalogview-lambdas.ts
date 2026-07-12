import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import { Construct } from 'constructs';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const CATALOGVIEW_DOCKERFILE = 'src/Services/CatalogView/CatalogView.Function/Dockerfile';

export interface CatalogViewLambdasProps {
  readonly catalogViewProductsTable: dynamodb.Table;
}

// Six EventBridge-triggered consumer Lambdas — none HTTP/AppSync-invoked (ADR-0030 reverted
// products/product back to Direct DynamoDB resolvers, so CatalogView no longer exposes any
// synchronously invoked Lambda). Each keeps `catalogview-products` in sync with a slice of another
// bounded context's writes via CDC.
export class CatalogViewLambdas extends Construct {
  public readonly productSyncConsumer: lambda.Function;
  public readonly productDeletedConsumer: lambda.Function;
  public readonly reviewAggregateConsumer: lambda.Function;
  public readonly reviewUpdateAggregateConsumer: lambda.Function;
  public readonly priceSyncConsumer: lambda.Function;
  public readonly categorySyncConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: CatalogViewLambdasProps) {
    super(scope, id);

    const { catalogViewProductsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // All six CatalogView Lambdas share the same image, built once.
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
      rule.addTarget(new targets.LambdaFunction(fn));
    };

    // -------------------------------------------------------------------------
    // 1. catalogview-product-sync-consumer
    //    Trigger: ProductSyncedEvent (Catalog's CDC stream publisher, ADR-0027 §1, ADR-0031).
    //    Upserts the product fields on catalogview-products — never touches
    //    Price/AverageRating/RatingCount/RatingSum/LastRatingEventId (ADR-0027 §3, ADR-0030).
    //    Deletes are handled by catalogview-product-deleted-consumer below (ADR-0031).
    // -------------------------------------------------------------------------
    this.productSyncConsumer = new lambda.DockerImageFunction(this, 'ProductSyncConsumer', {
      functionName: 'catalogview-product-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_CatalogProductSyncConsumer_Generated::CatalogProductSyncConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Consumes ProductSyncedEvent and upserts catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.productSyncConsumer);
    ruleFor(
      'ProductSync',
      this.productSyncConsumer,
      'catalogview-product-sync-consumer-rule',
      'ProductSyncedEvent',
      'Routes ProductSyncedEvent (source=duckstore) to catalogview-product-sync-consumer',
    );

    // -------------------------------------------------------------------------
    // 1b. catalogview-product-deleted-consumer
    //     Trigger: ProductDeletedEvent — the same thin event Pricing consumes (ADR-0031: named
    //     after the domain occurrence, no ChangeType discriminator). Deletes the product from the
    //     search index; write-only, never reads.
    // -------------------------------------------------------------------------
    this.productDeletedConsumer = new lambda.DockerImageFunction(this, 'ProductDeletedConsumer', {
      functionName: 'catalogview-product-deleted-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_ProductDeletedConsumer_Generated::ProductDeletedConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Consumes ProductDeletedEvent and removes the product from catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantWriteData(this.productDeletedConsumer);
    ruleFor(
      'ProductDeleted',
      this.productDeletedConsumer,
      'catalogview-product-deleted-consumer-rule',
      'ProductDeletedEvent',
      'Routes ProductDeletedEvent (source=duckstore) to catalogview-product-deleted-consumer',
    );

    // -------------------------------------------------------------------------
    // 2. catalogview-review-aggregate-consumer
    //    Trigger: ReviewCreatedEvent (Review's CDC stream publisher, ADR-0011).
    //    Two-step, non-atomic ADD + recompute average (ADR-0030 accepted trade-off 2).
    // -------------------------------------------------------------------------
    this.reviewAggregateConsumer = new lambda.DockerImageFunction(this, 'ReviewAggregateConsumer', {
      functionName: 'catalogview-review-aggregate-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_ReviewAggregateConsumer_Generated::ReviewAggregateConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Consumes ReviewCreatedEvent and folds the new rating into catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.reviewAggregateConsumer);
    ruleFor(
      'ReviewAggregate',
      this.reviewAggregateConsumer,
      'catalogview-review-aggregate-consumer-rule',
      'ReviewCreatedEvent',
      'Routes ReviewCreatedEvent (source=duckstore) to catalogview-review-aggregate-consumer',
    );

    // -------------------------------------------------------------------------
    // 3. catalogview-review-update-aggregate-consumer
    //    Trigger: ReviewUpdatedEvent (ADR-0029 — a customer editing an existing review).
    //    Sibling to ReviewAggregateConsumer: ratingCount unchanged, ratingSum moves by delta.
    // -------------------------------------------------------------------------
    this.reviewUpdateAggregateConsumer = new lambda.DockerImageFunction(
      this,
      'ReviewUpdateAggregateConsumer',
      {
        functionName: 'catalogview-review-update-aggregate-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: catalogViewCode([
          'CatalogView.Function::CatalogView.Function.Functions_ReviewUpdateAggregateConsumer_Generated::ReviewUpdateAggregateConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'Consumes ReviewUpdatedEvent and applies the rating delta to catalogview-products',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );
    catalogViewProductsTable.grantReadWriteData(this.reviewUpdateAggregateConsumer);
    ruleFor(
      'ReviewUpdateAggregate',
      this.reviewUpdateAggregateConsumer,
      'catalogview-review-update-aggregate-consumer-rule',
      'ReviewUpdatedEvent',
      'Routes ReviewUpdatedEvent (source=duckstore) to catalogview-review-update-aggregate-consumer',
    );

    // -------------------------------------------------------------------------
    // 4. catalogview-price-sync-consumer
    //    Trigger: PriceChangedEvent (Pricing's CDC stream publisher, ADR-0026/ADR-0028).
    //    Partial merge of price + payment-highlight fields — naturally idempotent (absolute values).
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // 5. catalogview-category-sync-consumer
    //    Trigger: CatalogCategorySyncEvent (a category rename in Catalog, ADR-0027 extension).
    //    Scan + per-matching-item UpdateItem (ADR-0030 — no _update_by_query equivalent).
    // -------------------------------------------------------------------------
    this.categorySyncConsumer = new lambda.DockerImageFunction(this, 'CategorySyncConsumer', {
      functionName: 'catalogview-category-sync-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: catalogViewCode([
        'CatalogView.Function::CatalogView.Function.Functions_CategorySyncConsumer_Generated::CategorySyncConsumer',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'Consumes CatalogCategorySyncEvent and rewrites the denormalized category name on catalogview-products',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });
    catalogViewProductsTable.grantReadWriteData(this.categorySyncConsumer);
    ruleFor(
      'CategorySync',
      this.categorySyncConsumer,
      'catalogview-category-sync-consumer-rule',
      'CatalogCategorySyncEvent',
      'Routes CatalogCategorySyncEvent (source=duckstore) to catalogview-category-sync-consumer',
    );

    // Note: products/product are AppSync direct DynamoDB resolvers against catalogview-products
    // (ADR-0030, reverses ADR-0027 §5) — not Lambdas — see infra/constructs/appsync-api.ts.
  }
}
