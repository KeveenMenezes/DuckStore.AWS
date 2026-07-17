import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const PRICING_DOCKERFILE = 'src/Services/Pricing/Pricing.Function/Dockerfile';

export interface PricingLambdasProps {
  readonly pricesTable: dynamodb.Table;
  readonly campaignsTable: dynamodb.Table;
  readonly productDiscountsTable: dynamodb.Table;
  readonly gatewayCostsTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
}

export class PricingLambdas extends Construct {
  public readonly getInstallmentPlan: lambda.Function;
  public readonly getBasketInstallmentPlan: lambda.Function;
  public readonly createCampaign: lambda.Function;
  public readonly endCampaign: lambda.Function;
  public readonly productDeletedConsumer: lambda.Function;
  public readonly priceStreamPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: PricingLambdasProps) {
    super(scope, id);

    const { pricesTable, campaignsTable, productDiscountsTable, gatewayCostsTable, processedEventsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for every async Pricing process; a non-empty
    // queue trips the pricing-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'pricing' });

    // All five Pricing Lambdas share the same image, built once.
    const pricingImage = new ecrAssets.DockerImageAsset(this, 'PricingImage', {
      directory: REPO_ROOT,
      file: PRICING_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/Pricing/Pricing.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const pricingCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(pricingImage.repository, {
        tagOrDigest: pricingImage.imageTag,
        cmd,
      });

    // Note: pricing-set-nominal-price (SetNominalPrice command) is gone — setNominalPrice is now
    // an AppSync direct DynamoDB UpdateItem resolver (ADR-0009); see appsync-api.ts and
    // graphql/resolvers/pricing/mutations/Mutation.setNominalPrice.js.

    // -------------------------------------------------------------------------
    // 1. pricing-get-installment-plan  (AppSync Invoke — Query.installmentPlanFor)
    //    Non-trivial calculation over simulated gateway fee/margin config — Lambda per ADR-0009.
    // -------------------------------------------------------------------------
    this.getInstallmentPlan = new lambda.DockerImageFunction(this, 'GetInstallmentPlan', {
      functionName: 'pricing-get-installment-plan',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_GetInstallmentPlan_Generated::GetInstallmentPlan',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Computes the max interest-free installments for a product\'s nominal price',
      environment: {
        Installments__ActiveProvider: 'Simulated',
        Installments__MinMarginPercent: '5',
      },
    });
    pricesTable.grantReadData(this.getInstallmentPlan);
    gatewayCostsTable.grantReadData(this.getInstallmentPlan);
    productDiscountsTable.grantReadData(this.getInstallmentPlan);

    // -------------------------------------------------------------------------
    // 1b. pricing-get-basket-installment-plan  (AppSync Invoke — Query.basketInstallmentPlan)
    //     Same cost-floor calculation, but summed across every cart item first — the whole cart
    //     is treated as one checkout transaction (ADR-0009).
    // -------------------------------------------------------------------------
    this.getBasketInstallmentPlan = new lambda.DockerImageFunction(this, 'GetBasketInstallmentPlan', {
      functionName: 'pricing-get-basket-installment-plan',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_GetBasketInstallmentPlan_Generated::GetBasketInstallmentPlan',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Computes the unified interest-free installment plan for an entire cart',
      environment: {
        Installments__ActiveProvider: 'Simulated',
        Installments__MinMarginPercent: '5',
      },
    });
    pricesTable.grantReadData(this.getBasketInstallmentPlan);
    gatewayCostsTable.grantReadData(this.getBasketInstallmentPlan);

    // -------------------------------------------------------------------------
    // 2. pricing-create-campaign  (AppSync Invoke — Mutation.createCampaign)
    //    Fans out a TransactWriteItems across campaigns + product-discounts (ADR-0026 §6).
    // -------------------------------------------------------------------------
    this.createCampaign = new lambda.DockerImageFunction(this, 'CreateCampaign', {
      functionName: 'pricing-create-campaign',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_CreateCampaign_Generated::CreateCampaign',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Creates a discount campaign and fans out product-discounts rows',
    });
    campaignsTable.grantReadWriteData(this.createCampaign);
    productDiscountsTable.grantReadWriteData(this.createCampaign);

    // -------------------------------------------------------------------------
    // 3. pricing-end-campaign  (AppSync Invoke — Mutation.endCampaign)
    //    Reads the campaign, then retracts its product-discounts rows transactionally.
    // -------------------------------------------------------------------------
    this.endCampaign = new lambda.DockerImageFunction(this, 'EndCampaign', {
      functionName: 'pricing-end-campaign',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_EndCampaign_Generated::EndCampaign',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Cancels a discount campaign and retracts its product-discounts rows',
    });
    campaignsTable.grantReadWriteData(this.endCampaign);
    productDiscountsTable.grantReadWriteData(this.endCampaign);

    // -------------------------------------------------------------------------
    // 4. pricing-product-deleted-consumer
    //    Trigger: EventBridge rule (ProductDeletedEvent, source=duckstore — ADR-0031: named after
    //    the domain occurrence, no ChangeType discriminator). Cleans up prices/product-discounts
    //    rows for the deleted product, idempotent via pricing-processed-events (ADR-0026 §5).
    // -------------------------------------------------------------------------
    this.productDeletedConsumer = new lambda.DockerImageFunction(
      this,
      'ProductDeletedConsumer',
      {
        functionName: 'pricing-product-deleted-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: pricingCode([
          'Pricing.Function::Pricing.Function.Functions_ProductDeletedConsumer_Generated::ProductDeletedConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Consumes ProductDeletedEvent and cleans up Pricing rows for the deleted product',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );
    pricesTable.grantWriteData(this.productDeletedConsumer);
    productDiscountsTable.grantWriteData(this.productDeletedConsumer);
    processedEventsTable.grantWriteData(this.productDeletedConsumer);

    const productDeletedRule = new events.Rule(this, 'ProductDeletedRule', {
      eventBus,
      ruleName: 'pricing-product-deleted-consumer-rule',
      description:
        'Routes ProductDeletedEvent (source=duckstore) to pricing-product-deleted-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['ProductDeletedEvent'],
      },
    });
    // Two failure paths, one queue: the async-invoke destination captures the
    // event when the Lambda keeps throwing; the rule-target DLQ captures events
    // EventBridge could not deliver to the Lambda at all.
    this.productDeletedConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    productDeletedRule.addTarget(
      new targets.LambdaFunction(this.productDeletedConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );

    // -------------------------------------------------------------------------
    // 5. pricing-prices-event-publisher
    //    Trigger: DynamoDB Streams on prices. CDC: publishes a single PriceChangedEvent (nominal
    //    price + payment badge computed from the active GatewayCost) to EventBridge on
    //    INSERT/MODIFY so CatalogView syncs both in one merge (ADR-0026/0027/0028).
    // -------------------------------------------------------------------------
    this.priceStreamPublisher = new lambda.DockerImageFunction(this, 'PriceStreamPublisher', {
      functionName: 'pricing-prices-event-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_PriceStreamPublisher_Generated::PriceStreamPublisher',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'CDC: reads DynamoDB Streams on prices and publishes PriceChangedEvent (price + payment badge) to EventBridge',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
        Installments__ActiveProvider: 'Simulated',
        Installments__MinMarginPercent: '5',
      },
    });

    this.priceStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(pricesTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (shard/sequence
        // metadata, not the payload — redrive by re-reading the stream).
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    eventBus.grantPutEventsTo(this.priceStreamPublisher);
    gatewayCostsTable.grantReadData(this.priceStreamPublisher);
    productDiscountsTable.grantReadData(this.priceStreamPublisher);

    // Note: nominalPriceFor/setNominalPrice/setGatewayCost are AppSync direct DynamoDB
    // resolvers (ADR-0009), not Lambdas — see infra/constructs/appsync-api.ts.
  }
}
