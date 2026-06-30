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

const REPO_ROOT = path.join(__dirname, '..', '..');
const ORDERING_DOCKERFILE = 'src/Services/Ordering/Ordering.Function/Dockerfile';

export interface OrderingLambdasProps {
  readonly orderingTable: dynamodb.Table;
  readonly processedEventsTable: dynamodb.Table;
}

export class OrderingLambdas extends Construct {
  public readonly basketCheckoutConsumer: lambda.Function;
  public readonly orderCreatedPublisher: lambda.Function;
  public readonly getOrdersByCustomer: lambda.Function;
  public readonly deleteOrder: lambda.Function;
  public readonly getOrdersByCustomerUrl: lambda.FunctionUrl;
  public readonly deleteOrderUrl: lambda.FunctionUrl;

  constructor(scope: Construct, id: string, props: OrderingLambdasProps) {
    super(scope, id);

    const { orderingTable, processedEventsTable } = props;

    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

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

    // -------------------------------------------------------------------------
    // 1. ordering-basket-checkout-consumer
    //    Trigger: EventBridge rule (BasketCheckoutEvent, source=duckstore)
    //    Writes the new Order + idempotency record atomically via TransactWriteItems.
    //    EventBridge sets evt.Id as the idempotency key (ADR-0011).
    // -------------------------------------------------------------------------
    this.basketCheckoutConsumer = new lambda.DockerImageFunction(
      this,
      'BasketCheckoutConsumer',
      {
        functionName: 'ordering-basket-checkout-consumer',
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.EventsIntegration.Consumer.BasketCheckoutConsumerFunction::FunctionHandler',
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
    basketCheckoutRule.addTarget(
      new targets.LambdaFunction(this.basketCheckoutConsumer),
    );

    // -------------------------------------------------------------------------
    // 2. ordering-order-created-publisher
    //    Trigger: DynamoDB Streams on ordering table (NEW_IMAGE, CDC — ADR-0005)
    //    On INSERT records of Type=Order, publishes OrderCreatedEvent to EventBridge.
    //    Gated by FeatureManagement__OrderFullfilment=true.
    // -------------------------------------------------------------------------
    this.orderCreatedPublisher = new lambda.DockerImageFunction(
      this,
      'OrderCreatedPublisher',
      {
        functionName: 'ordering-order-created-publisher',
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.EventsIntegration.Publisher.OrderCreatedPublisherFunction::FunctionHandler',
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
      }),
    );

    // GetItem by orderId after a stream INSERT.
    orderingTable.grantReadData(this.orderCreatedPublisher);
    eventBus.grantPutEventsTo(this.orderCreatedPublisher);

    // -------------------------------------------------------------------------
    // 3. ordering-get-orders-by-customer  (HTTP API)
    //    Trigger: Lambda Function URL
    //    Queries GSI1 (GSI1PK=CUSTOMER#{id}, GSI1SK=CreatedAt desc).
    // -------------------------------------------------------------------------
    this.getOrdersByCustomer = new lambda.DockerImageFunction(
      this,
      'GetOrdersByCustomer',
      {
        functionName: 'ordering-get-orders-by-customer',
        architecture: DOTNET_ARCH,
        code: orderingCode([
          'Ordering.Function::Ordering.Function.Functions_GetOrdersByCustomer_Generated::GetOrdersByCustomer',
        ]),
        timeout: cdk.Duration.seconds(30),
        memorySize: 512,
        description: 'Returns all orders for a customer via GSI1 query',
        environment: {
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    orderingTable.grantReadData(this.getOrdersByCustomer);

    this.getOrdersByCustomerUrl = this.getOrdersByCustomer.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // 4. ordering-delete-order  (HTTP API)
    //    Trigger: Lambda Function URL
    //    DeleteItem by Id.
    // -------------------------------------------------------------------------
    this.deleteOrder = new lambda.DockerImageFunction(this, 'DeleteOrder', {
      functionName: 'ordering-delete-order',
      architecture: DOTNET_ARCH,
      code: orderingCode([
        'Ordering.Function::Ordering.Function.Functions_DeleteOrder_Generated::DeleteOrder',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Deletes an order by Id',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });

    orderingTable.grantWriteData(this.deleteOrder);

    this.deleteOrderUrl = this.deleteOrder.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });
  }
}
