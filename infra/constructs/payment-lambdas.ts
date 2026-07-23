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
const PAYMENT_DOCKERFILE = 'src/Services/Payment/Payment.Function/Dockerfile';
const PAYMENTGATEWAY_DOCKERFILE = 'src/Services/PaymentGateway/PaymentGateway.Function/Dockerfile';

export interface PaymentLambdasProps {
  readonly paymentsTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
}

export class PaymentLambdas extends Construct {
  public readonly basketCheckoutConsumer: lambda.Function;
  public readonly paymentRequestedPublisher: lambda.Function;
  public readonly paymentAuthorizedConsumer: lambda.Function;
  public readonly paymentDeclinedConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: PaymentLambdasProps) {
    super(scope, id);

    const { paymentsTable, processedEventsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for every async Payment process; a non-empty
    // queue trips the payment-dlq-not-empty alarm → duckstore-alerts (ADR-0015).
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'payment' });

    // All four Payment Lambdas share the same image, built once.
    const paymentImage = new ecrAssets.DockerImageAsset(this, 'PaymentImage', {
      directory: REPO_ROOT,
      file: PAYMENT_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/Payment/Payment.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const paymentCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(paymentImage.repository, {
        tagOrDigest: paymentImage.imageTag,
        cmd,
      });

    // 1. payment-basket-checkout-consumer
    //    Trigger: EventBridge rule (BasketCheckoutEvent, source=duckstore)
    //    Consumes BasketCheckoutEvent directly (not OrderCreatedEvent) so card data never
    //    has to be persisted or republished by Ordering — Ordering and Payment are
    //    independent, parallel consumers of the same event (ADR-0038).
    //    Writes the new Payment (Status=Pending) + idempotency record atomically via
    //    TransactWriteItems. EventBridge sets evt.Id as the idempotency key.
    this.basketCheckoutConsumer = new lambda.DockerImageFunction(
      this,
      'BasketCheckoutConsumer',
      {
        functionName: 'payment-basket-checkout-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: paymentCode([
          'Payment.Function::Payment.Function.Functions_BasketCheckoutConsumer_Generated::BasketCheckoutConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'Consumes BasketCheckoutEvent and creates a Pending Payment idempotently (ADR-0025/0038)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    paymentsTable.grantWriteData(this.basketCheckoutConsumer);
    processedEventsTable.grantWriteData(this.basketCheckoutConsumer);

    const basketCheckoutRule = new events.Rule(this, 'BasketCheckoutRule', {
      eventBus,
      ruleName: 'payment-basket-checkout-consumer-rule',
      description:
        'Routes BasketCheckoutEvent (source=duckstore) to payment-basket-checkout-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['BasketCheckoutEvent'],
      },
    });
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

    // 2. payment-requested-publisher
    //    Trigger: DynamoDB Streams on payments table (NEW_AND_OLD_IMAGES, CDC — ADR-0005/0019)
    //    Rule-based publisher: PaymentRequestedRule emits PaymentRequestedEvent on INSERT of a
    //    Pending payment.
    this.paymentRequestedPublisher = new lambda.DockerImageFunction(
      this,
      'PaymentRequestedPublisher',
      {
        functionName: 'payment-requested-publisher',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: paymentCode([
          'Payment.Function::Payment.Function.Functions_PaymentStreamPublisher_Generated::PaymentStreamPublisher',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'CDC: reads DynamoDB Streams on payments and publishes PaymentRequestedEvent to EventBridge',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    this.paymentRequestedPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(paymentsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    paymentsTable.grantReadData(this.paymentRequestedPublisher);
    eventBus.grantPutEventsTo(this.paymentRequestedPublisher);

    // 3/4. payment-result-authorized-consumer / payment-result-declined-consumer
    //    Trigger: EventBridge rule (PaymentAuthorizedEvent / PaymentDeclinedEvent, source=duckstore)
    //    Both transition the Payment row Pending -> Authorized/Declined, idempotent via the
    //    same payment-processed-events inbox. Ordering's own PaymentResult consumer (in the
    //    Ordering stack) is an independent subscriber of the same two events.
    this.paymentAuthorizedConsumer = new lambda.DockerImageFunction(
      this,
      'PaymentAuthorizedConsumer',
      {
        functionName: 'payment-result-authorized-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: paymentCode([
          'Payment.Function::Payment.Function.Functions_PaymentAuthorizedConsumer_Generated::PaymentAuthorizedConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Applies PaymentAuthorizedEvent to the Payment row (Pending -> Authorized)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    paymentsTable.grantReadWriteData(this.paymentAuthorizedConsumer);
    processedEventsTable.grantWriteData(this.paymentAuthorizedConsumer);

    const paymentAuthorizedRule = new events.Rule(this, 'PaymentAuthorizedRule', {
      eventBus,
      ruleName: 'payment-result-authorized-consumer-rule',
      description:
        'Routes PaymentAuthorizedEvent (source=duckstore) to payment-result-authorized-consumer',
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
        functionName: 'payment-result-declined-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: paymentCode([
          'Payment.Function::Payment.Function.Functions_PaymentDeclinedConsumer_Generated::PaymentDeclinedConsumer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Applies PaymentDeclinedEvent to the Payment row (Pending -> Declined)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    paymentsTable.grantReadWriteData(this.paymentDeclinedConsumer);
    processedEventsTable.grantWriteData(this.paymentDeclinedConsumer);

    const paymentDeclinedRule = new events.Rule(this, 'PaymentDeclinedRule', {
      eventBus,
      ruleName: 'payment-result-declined-consumer-rule',
      description:
        'Routes PaymentDeclinedEvent (source=duckstore) to payment-result-declined-consumer',
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
  }
}

// PaymentGateway.Function is registered here too rather than in its own construct file: it
// owns no DynamoDB table, no seeder, no idempotency inbox (ADR-0025) — a single Lambda
// doesn't warrant a near-empty construct of its own. It gets its own DLQ (one per bounded
// context, ADR-0015) since it is a genuinely separate deployable unit from Payment.
export class PaymentGatewayLambdas extends Construct {
  public readonly paymentRequestedConsumer: lambda.Function;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'paymentgateway' });

    const paymentGatewayImage = new ecrAssets.DockerImageAsset(this, 'PaymentGatewayImage', {
      directory: REPO_ROOT,
      file: PAYMENTGATEWAY_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/PaymentGateway/PaymentGateway.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });

    // payment-requested-consumer
    //    Trigger: EventBridge rule (PaymentRequestedEvent, source=duckstore)
    //    Stateless simulated gateway: the authorize/decline decision is a pure function of
    //    (CardNumber, Amount) — no persistence, no idempotency inbox needed (ADR-0025 §1).
    this.paymentRequestedConsumer = new lambda.DockerImageFunction(
      this,
      'PaymentRequestedConsumer',
      {
        functionName: 'paymentgateway-consumer',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        code: lambda.DockerImageCode.fromEcr(paymentGatewayImage.repository, {
          tagOrDigest: paymentGatewayImage.imageTag,
          cmd: [
            'PaymentGateway.Function::PaymentGateway.Function.Functions_PaymentRequestedConsumer_Generated::PaymentRequestedConsumer',
          ],
        }),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description:
          'Simulated payment gateway: decides authorize/decline and publishes the result (ADR-0025)',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    eventBus.grantPutEventsTo(this.paymentRequestedConsumer);

    const paymentRequestedRule = new events.Rule(this, 'PaymentRequestedRule', {
      eventBus,
      ruleName: 'paymentgateway-consumer-rule',
      description: 'Routes PaymentRequestedEvent (source=duckstore) to paymentgateway-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['PaymentRequestedEvent'],
      },
    });
    this.paymentRequestedConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    paymentRequestedRule.addTarget(
      new targets.LambdaFunction(this.paymentRequestedConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );
  }
}
