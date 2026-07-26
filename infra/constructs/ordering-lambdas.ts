import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const ORDERING_DOCKERFILE = 'src/Services/Ordering/Ordering.Function/Dockerfile';

export interface OrderingLambdasProps {
  readonly orderingTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
}

export class OrderingLambdas extends Construct {
  public readonly basketCheckoutConsumer: lambda.Function;
  public readonly orderCreatedPublisher: lambda.Function;
  public readonly paymentAuthorizedConsumer: lambda.Function;
  public readonly paymentDeclinedConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: OrderingLambdasProps) {
    super(scope, id);

    const { orderingTable, processedEventsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for every async Ordering process; a non-empty
    // queue trips the ordering-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'ordering' });

    // All four Ordering Lambdas share the same image, built once.
    const orderingImage = new ecrAssets.DockerImageAsset(this, 'OrderingImage', {
      directory: REPO_ROOT,
      file: ORDERING_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/Ordering/Ordering.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const orderingCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(orderingImage.repository, {
        tagOrDigest: orderingImage.imageTag,
        cmd,
      });

    // 1. ordering-basket-checkout-consumer
    //    Trigger: EventBridge rule (BasketCheckoutEvent, source=duckstore)
    //    Writes the new Order + idempotency record atomically via TransactWriteItems.
    //    EventBridge sets evt.Id as the idempotency key (ADR-0011).
    this.basketCheckoutConsumer = new lambda.DockerImageFunction(
      this,
      'BasketCheckoutConsumer',
      {
        functionName: 'ordering-basket-checkout-consumer',
        // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.Functions_BasketCheckoutConsumer_Generated::BasketCheckoutConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'Consumes BasketCheckoutEvent and creates an Order idempotently (ADR-0011, TransactWriteItems)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    // Order write (PutItem inside TransactWriteItems).
    orderingTable.grantWriteData(this.basketCheckoutConsumer);
    // Idempotency inbox write + condition check (both are Put inside TransactWriteItems).
    processedEventsTable.grantWriteData(this.basketCheckoutConsumer);

    const basketCheckoutRule = new events.Rule(this, 'BasketCheckoutRule', {
      eventBus,
      ruleName: 'ordering-basket-checkout-consumer-rule',
      description:
        'Routes BasketCheckoutEvent (source=duckstore) to ordering-basket-checkout-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['BasketCheckoutEvent'],
      },
    });
    // Two failure paths, one queue: the async-invoke destination captures the
    // event when the Lambda keeps throwing; the rule-target DLQ captures events
    // EventBridge could not deliver to the Lambda at all.
    this.basketCheckoutConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    basketCheckoutRule.addTarget(
      new targets.LambdaFunction(this.basketCheckoutConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );

    // 2. ordering-stream-publisher
    //    Trigger: DynamoDB Streams on ordering table (NEW_AND_OLD_IMAGES, CDC — ADR-0005/0019)
    //    Rule-based publisher (ADR-0019): OrderCreatedRule emits OrderCreatedEvent on INSERT of Type=Order.
    //    Gated by FeatureManagement__OrderFullfilment=true.
    this.orderCreatedPublisher = new lambda.DockerImageFunction(
      this,
      'OrderCreatedPublisher',
      {
        functionName: 'ordering-stream-publisher',
        // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.Functions_OrderStreamPublisher_Generated::OrderStreamPublisher',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'CDC: reads DynamoDB Streams on ordering and publishes OrderCreatedEvent to EventBridge',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
          // Enable the OrderFulfillment feature gate so the publisher actually fires.
          FeatureManagement__OrderFullfilment: 'true',
        },
      },
    );

    this.orderCreatedPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(orderingTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (shard/sequence
        // metadata, not the payload — redrive by re-reading the stream).
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    // GetItem by orderId after a stream INSERT.
    orderingTable.grantReadData(this.orderCreatedPublisher);
    eventBus.grantPutEventsTo(this.orderCreatedPublisher);

    // 3/4. ordering-payment-authorized-consumer / ordering-payment-declined-consumer
    //    Trigger: EventBridge rule (PaymentAuthorizedEvent / PaymentDeclinedEvent, source=duckstore)
    //    Both transition the Order Pending -> Completed/Cancelled, idempotent via the same
    //    ordering-processed-events inbox (TransactWriteItems). Payment's own PaymentResult
    //    consumers (in the Payment stack) are independent subscribers of the same two events.
    this.paymentAuthorizedConsumer = new lambda.DockerImageFunction(
      this,
      'PaymentAuthorizedConsumer',
      {
        functionName: 'ordering-payment-authorized-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.Functions_OrderPaymentAuthorizedConsumer_Generated::OrderPaymentAuthorizedConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Applies PaymentAuthorizedEvent to the Order (Pending -> Completed)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    orderingTable.grantReadWriteData(this.paymentAuthorizedConsumer);
    processedEventsTable.grantWriteData(this.paymentAuthorizedConsumer);

    const paymentAuthorizedRule = new events.Rule(this, 'PaymentAuthorizedRule', {
      eventBus,
      ruleName: 'ordering-payment-authorized-consumer-rule',
      description:
        'Routes PaymentAuthorizedEvent (source=duckstore) to ordering-payment-authorized-consumer',
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

    this.paymentDeclinedConsumer = new lambda.DockerImageFunction(
      this,
      'PaymentDeclinedConsumer',
      {
        functionName: 'ordering-payment-declined-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.Functions_OrderPaymentDeclinedConsumer_Generated::OrderPaymentDeclinedConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Applies PaymentDeclinedEvent to the Order (Pending -> Cancelled)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    orderingTable.grantReadWriteData(this.paymentDeclinedConsumer);
    processedEventsTable.grantWriteData(this.paymentDeclinedConsumer);

    const paymentDeclinedRule = new events.Rule(this, 'PaymentDeclinedRule', {
      eventBus,
      ruleName: 'ordering-payment-declined-consumer-rule',
      description:
        'Routes PaymentDeclinedEvent (source=duckstore) to ordering-payment-declined-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['PaymentDeclinedEvent'],
      },
    });
    this.paymentDeclinedConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    paymentDeclinedRule.addTarget(
      new targets.LambdaFunction(this.paymentDeclinedConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );

    // Note: ordersByCustomer (read) and deleteOrder (delete) are AppSync direct DynamoDB
    // resolvers (ADR-0009), not Lambdas — see infra/constructs/appsync-api.ts.
  }
}
