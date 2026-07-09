import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import { Construct } from 'constructs';

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
  public readonly setNominalPrice: lambda.Function;
  public readonly getInstallmentPlan: lambda.Function;
  public readonly getBasketInstallmentPlan: lambda.Function;
  public readonly createCampaign: lambda.Function;
  public readonly endCampaign: lambda.Function;
  public readonly catalogProductRemovedConsumer: lambda.Function;
  public readonly priceStreamPublisher: lambda.Function;
  public readonly setGatewayCost: lambda.Function;

  constructor(scope: Construct, id: string, props: PricingLambdasProps) {
    super(scope, id);

    const { pricesTable, campaignsTable, productDiscountsTable, gatewayCostsTable, processedEventsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

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

    // -------------------------------------------------------------------------
    // 1. pricing-set-nominal-price  (AppSync Invoke — Mutation.setNominalPrice)
    //    Decoupled from Catalog's createProduct/updateProduct (ADR-0026 §4).
    // -------------------------------------------------------------------------
    this.setNominalPrice = new lambda.DockerImageFunction(this, 'SetNominalPrice', {
      functionName: 'pricing-set-nominal-price',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_SetNominalPrice_Generated::SetNominalPrice',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Sets or updates a product\'s nominal price',
    });
    pricesTable.grantReadWriteData(this.setNominalPrice);

    // -------------------------------------------------------------------------
    // 2. pricing-get-installment-plan  (AppSync Invoke — Query.installmentPlanFor)
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

    // -------------------------------------------------------------------------
    // 2b. pricing-get-basket-installment-plan  (AppSync Invoke — Query.basketInstallmentPlan)
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
    // 3. pricing-create-campaign  (AppSync Invoke — Mutation.createCampaign)
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
    // 4. pricing-end-campaign  (AppSync Invoke — Mutation.endCampaign)
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
    // 5. pricing-catalog-product-removed-consumer
    //    Trigger: EventBridge rule (CatalogUpdatedEvent, source=duckstore, ChangeType=REMOVE
    //    filtered in-handler). Cleans up prices/product-discounts rows for the deleted product,
    //    idempotent via pricing-processed-events (ADR-0026 §5).
    // -------------------------------------------------------------------------
    this.catalogProductRemovedConsumer = new lambda.DockerImageFunction(
      this,
      'CatalogProductRemovedConsumer',
      {
        functionName: 'pricing-catalog-product-removed-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: pricingCode([
          'Pricing.Function::Pricing.Function.Functions_CatalogProductRemovedConsumer_Generated::CatalogProductRemovedConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Consumes CatalogUpdatedEvent and cleans up Pricing rows for a removed product',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );
    pricesTable.grantWriteData(this.catalogProductRemovedConsumer);
    productDiscountsTable.grantWriteData(this.catalogProductRemovedConsumer);
    processedEventsTable.grantWriteData(this.catalogProductRemovedConsumer);

    const catalogProductRemovedRule = new events.Rule(this, 'CatalogProductRemovedRule', {
      eventBus,
      ruleName: 'pricing-catalog-product-removed-consumer-rule',
      description:
        'Routes CatalogUpdatedEvent (source=duckstore) to pricing-catalog-product-removed-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['CatalogUpdatedEvent'],
      },
    });
    catalogProductRemovedRule.addTarget(
      new targets.LambdaFunction(this.catalogProductRemovedConsumer),
    );

    // -------------------------------------------------------------------------
    // 6. pricing-prices-event-publisher
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
      }),
    );

    eventBus.grantPutEventsTo(this.priceStreamPublisher);
    gatewayCostsTable.grantReadData(this.priceStreamPublisher);

    // -------------------------------------------------------------------------
    // 7. pricing-set-gateway-cost  (AppSync Invoke — Mutation.setGatewayCost)
    //    Configures a payment-gateway provider's flat fee, à vista rate, and per-installment-count
    //    rate table (ADR-0028) — validated server-side before writing, like setNominalPrice.
    // -------------------------------------------------------------------------
    this.setGatewayCost = new lambda.DockerImageFunction(this, 'SetGatewayCost', {
      functionName: 'pricing-set-gateway-cost',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: pricingCode([
        'Pricing.Function::Pricing.Function.Functions_SetGatewayCost_Generated::SetGatewayCost',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Configures a payment-gateway provider\'s cost table',
    });
    gatewayCostsTable.grantReadWriteData(this.setGatewayCost);

    // Note: nominalPriceFor/currentDiscountForProduct are AppSync direct DynamoDB resolvers
    // (ADR-0009), not Lambdas — see infra/constructs/appsync-api.ts.
  }
}
