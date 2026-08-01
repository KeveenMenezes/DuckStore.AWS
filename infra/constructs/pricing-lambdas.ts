import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';
import {
  DOTNET_ARCH,
  DOTNET_MEMORY_MB,
  DOTNET_RUNTIME,
  dotnetLambdaCode,
} from './dotnet-lambda-code';
import { REWARD_CURRENCY_PER_UNIT, REWARD_EXPIRY_DAYS, REWARD_POINTS_PER_UNIT } from './reward-config';


export interface PricingLambdasProps {
  readonly pricesTable: dynamodb.Table;
  readonly campaignsTable: dynamodb.Table;
  readonly productDiscountsTable: dynamodb.Table;
  readonly gatewayCostsTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
  readonly customerDiscountsTable: dynamodb.Table;
}

export class PricingLambdas extends Construct {
  public readonly getInstallmentPlan: lambda.Function;
  public readonly getBasketInstallmentPlan: lambda.Function;
  public readonly createCampaign: lambda.Function;
  public readonly endCampaign: lambda.Function;
  public readonly productDeletedConsumer: lambda.Function;
  public readonly priceStreamPublisher: lambda.Function;
  public readonly productDiscountStreamPublisher: lambda.Function;
  public readonly pointsRedeemedConsumer: lambda.Function;
  public readonly paymentAuthorizedConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: PricingLambdasProps) {
    super(scope, id);

    const {
      pricesTable, campaignsTable, productDiscountsTable, gatewayCostsTable, processedEventsTable,
      customerDiscountsTable,
    } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for every async Pricing process; a non-empty
    // queue trips the pricing-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'pricing' });

    // One ZIP per service, shared by all its functions; each Lambda selects its
    // handler through ANNOTATIONS_HANDLER instead of a Docker cmd override (ADR-0042).
    const pricingCode = dotnetLambdaCode(
      'src/Services/Pricing',
      'src/Services/Pricing/Pricing.Function/Pricing.Function.csproj',
    );

    // Note: pricing-set-nominal-price (SetNominalPrice command) is gone — setNominalPrice is now
    // an AppSync direct DynamoDB UpdateItem resolver (ADR-0009); see appsync-api.ts and
    // graphql/resolvers/pricing/mutations/Mutation.setNominalPrice.js.

    // 1. pricing-get-installment-plan  (AppSync Invoke — Query.installmentPlanFor)
    //    Non-trivial calculation over simulated gateway fee/margin config — Lambda per ADR-0009.
    this.getInstallmentPlan = new lambda.Function(this, 'GetInstallmentPlan', {
      functionName: 'pricing-get-installment-plan',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Computes the max interest-free installments for a product\'s nominal price',
      environment: {
        ANNOTATIONS_HANDLER: 'GetInstallmentPlan',
        Installments__ActiveProvider: 'Simulated',
        Installments__MinMarginPercent: '5',
      },
    });
    pricesTable.grantReadData(this.getInstallmentPlan);
    gatewayCostsTable.grantReadData(this.getInstallmentPlan);
    productDiscountsTable.grantReadData(this.getInstallmentPlan);

    // 1b. pricing-get-basket-installment-plan  (AppSync Invoke — Query.basketInstallmentPlan)
    //     Same cost-floor calculation, but summed across every cart item first — the whole cart
    //     is treated as one checkout transaction (ADR-0009).
    this.getBasketInstallmentPlan = new lambda.Function(this, 'GetBasketInstallmentPlan', {
      functionName: 'pricing-get-basket-installment-plan',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Computes the unified interest-free installment plan for an entire cart',
      environment: {
        ANNOTATIONS_HANDLER: 'GetBasketInstallmentPlan',
        Installments__ActiveProvider: 'Simulated',
        Installments__MinMarginPercent: '5',
      },
    });
    pricesTable.grantReadData(this.getBasketInstallmentPlan);
    gatewayCostsTable.grantReadData(this.getBasketInstallmentPlan);
    productDiscountsTable.grantReadData(this.getBasketInstallmentPlan);
    // Read-only: the coupon is validated and priced here, but burned by
    // pricing-payment-authorized-consumer alone (ADR-0046 §5/§6).
    customerDiscountsTable.grantReadData(this.getBasketInstallmentPlan);

    // 2. pricing-create-campaign  (AppSync Invoke — Mutation.createCampaign)
    //    Fans out a TransactWriteItems across campaigns + product-discounts (ADR-0026 §6).
    this.createCampaign = new lambda.Function(this, 'CreateCampaign', {
      functionName: 'pricing-create-campaign',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Creates a discount campaign and fans out product-discounts rows',
      environment: { ANNOTATIONS_HANDLER: 'CreateCampaign' },
    });
    campaignsTable.grantReadWriteData(this.createCampaign);
    productDiscountsTable.grantReadWriteData(this.createCampaign);

    // 3. pricing-end-campaign  (AppSync Invoke — Mutation.endCampaign)
    //    Reads the campaign, then retracts its product-discounts rows transactionally.
    this.endCampaign = new lambda.Function(this, 'EndCampaign', {
      functionName: 'pricing-end-campaign',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Cancels a discount campaign and retracts its product-discounts rows',
      environment: { ANNOTATIONS_HANDLER: 'EndCampaign' },
    });
    campaignsTable.grantReadWriteData(this.endCampaign);
    productDiscountsTable.grantReadWriteData(this.endCampaign);

    // 4. pricing-product-deleted-consumer
    //    Trigger: EventBridge rule (ProductDeletedEvent, source=duckstore — ADR-0031: named after
    //    the domain occurrence, no ChangeType discriminator). Cleans up prices/product-discounts
    //    rows for the deleted product, idempotent via pricing-processed-events (ADR-0026 §5).
    this.productDeletedConsumer = new lambda.Function(
      this,
      'ProductDeletedConsumer',
      {
        functionName: 'pricing-product-deleted-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
        timeout: cdk.Duration.seconds(30),
        memorySize: DOTNET_MEMORY_MB,
        description: 'Consumes ProductDeletedEvent and cleans up Pricing rows for the deleted product',
        environment: {
        ANNOTATIONS_HANDLER: 'ProductDeletedConsumer',
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

    // 4b. pricing-points-redeemed-consumer
    //    Trigger: EventBridge rule (PointsRedeemedEvent, source=duckstore). Mints a
    //    customer-discounts row — Amount is computed here from Rewards config and nowhere else
    //    (ADR-0046 §1, §4). Idempotent via pricing-processed-events, same shape as
    //    pricing-product-deleted-consumer.
    this.pointsRedeemedConsumer = new lambda.Function(this, 'PointsRedeemedConsumer', {
      functionName: 'pricing-points-redeemed-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Consumes PointsRedeemedEvent and mints a customer-discounts row',
      environment: {
        ANNOTATIONS_HANDLER: 'PointsRedeemedConsumer',
        Rewards__PointsPerUnit: REWARD_POINTS_PER_UNIT.toString(),
        Rewards__CurrencyPerUnit: REWARD_CURRENCY_PER_UNIT.toString(),
        Rewards__ExpiryDays: REWARD_EXPIRY_DAYS.toString(),
      },
    });
    customerDiscountsTable.grantWriteData(this.pointsRedeemedConsumer);
    processedEventsTable.grantWriteData(this.pointsRedeemedConsumer);

    const pointsRedeemedRule = new events.Rule(this, 'PointsRedeemedRule', {
      eventBus,
      ruleName: 'pricing-points-redeemed-consumer-rule',
      description: 'Routes PointsRedeemedEvent (source=duckstore) to pricing-points-redeemed-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['PointsRedeemedEvent'],
      },
    });
    this.pointsRedeemedConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    pointsRedeemedRule.addTarget(
      new targets.LambdaFunction(this.pointsRedeemedConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );

    // 4c. pricing-payment-authorized-consumer
    //    Trigger: EventBridge rule (PaymentAuthorizedEvent, source=duckstore — the same event
    //    ordering-payment-authorized-consumer reacts to; distinct functionName so the two rules
    //    don't collide). Burns (Issued -> Consumed) the customer discount used at checkout, if any
    //    (ADR-0046 §6). Nothing subscribes to PaymentDeclinedEvent — a decline leaves the discount
    //    Issued and reusable. Idempotent via pricing-processed-events; the shared idempotent
    //    consumer's ConditionalCheckFailed catch-all also covers the double-spend case as a silent
    //    no-op.
    this.paymentAuthorizedConsumer = new lambda.Function(this, 'PaymentAuthorizedConsumer', {
      functionName: 'pricing-payment-authorized-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Burns the customer discount used at checkout when its payment is authorized',
      environment: { ANNOTATIONS_HANDLER: 'PaymentAuthorizedConsumer' },
    });
    customerDiscountsTable.grantWriteData(this.paymentAuthorizedConsumer);
    processedEventsTable.grantWriteData(this.paymentAuthorizedConsumer);

    const paymentAuthorizedRule = new events.Rule(this, 'PricingPaymentAuthorizedRule', {
      eventBus,
      ruleName: 'pricing-payment-authorized-consumer-rule',
      description: 'Routes PaymentAuthorizedEvent (source=duckstore) to pricing-payment-authorized-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['PaymentAuthorizedEvent'],
      },
    });
    this.paymentAuthorizedConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    paymentAuthorizedRule.addTarget(
      new targets.LambdaFunction(this.paymentAuthorizedConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );

    // 5. pricing-prices-stream-publisher
    //    Trigger: DynamoDB Streams on prices. CDC: publishes a single PriceChangedEvent (nominal
    //    price + payment badge computed from the active GatewayCost) to EventBridge on
    //    INSERT/MODIFY so CatalogView syncs both in one merge (ADR-0026/0027/0028).
    this.priceStreamPublisher = new lambda.Function(this, 'PriceStreamPublisher', {
      functionName: 'pricing-prices-stream-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'CDC: reads DynamoDB Streams on prices and publishes PriceChangedEvent (price + payment badge) to EventBridge',
      environment: {
        ANNOTATIONS_HANDLER: 'PriceStreamPublisher',
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

    // 6. pricing-product-discounts-stream-publisher
    //    Trigger: DynamoDB Streams on product-discounts (KEYS_ONLY). CDC: publishes
    //    ProductDiscountChangedEvent whenever a campaign starts (INSERT), is ended (REMOVE) or
    //    expires via TTL (also REMOVE), carrying the recomputed payment highlights so CatalogView
    //    refreshes the catalog price. Without this trigger a campaign never reaches the search
    //    document at all, because campaigns never write to `prices` (ADR-0044).
    this.productDiscountStreamPublisher = new lambda.Function(this, 'ProductDiscountStreamPublisher', {
      functionName: 'pricing-product-discounts-stream-publisher',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: pricingCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description:
        'CDC: reads DynamoDB Streams on product-discounts and publishes ProductDiscountChangedEvent when a campaign starts, ends or expires',
      environment: {
        ANNOTATIONS_HANDLER: 'ProductDiscountStreamPublisher',
        EventBridge__BusName: eventBus.eventBusName,
        Installments__ActiveProvider: 'Simulated',
        Installments__MinMarginPercent: '5',
      },
    });

    this.productDiscountStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(productDiscountsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    eventBus.grantPutEventsTo(this.productDiscountStreamPublisher);
    // Reads the product's cost/nominal price to recompute, the gateway's fee table, and the
    // discount row itself (absent on a REMOVE, which is what yields the undiscounted figures).
    pricesTable.grantReadData(this.productDiscountStreamPublisher);
    gatewayCostsTable.grantReadData(this.productDiscountStreamPublisher);
    productDiscountsTable.grantReadData(this.productDiscountStreamPublisher);

    // Note: nominalPriceFor/setNominalPrice/setGatewayCost are AppSync direct DynamoDB
    // resolvers (ADR-0009), not Lambdas — see infra/constructs/appsync-api.ts.
  }
}
