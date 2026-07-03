import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import { Construct } from 'constructs';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const BASKET_DOCKERFILE = 'src/Services/Basket/Basket.Function/Dockerfile';

export interface BasketLambdasProps {
  readonly shoppingCartsTable: dynamodb.Table;
  readonly couponsTable: dynamodb.Table;
}

export class BasketLambdas extends Construct {
  public readonly streamPublisher: lambda.Function;
  public readonly storeBasket: lambda.Function;
  public readonly checkoutBasket: lambda.Function;
  public readonly mergeBasket: lambda.Function;
  public readonly storeBasketUrl: lambda.FunctionUrl;
  public readonly checkoutBasketUrl: lambda.FunctionUrl;

  constructor(scope: Construct, id: string, props: BasketLambdasProps) {
    super(scope, id);

    const { shoppingCartsTable, couponsTable } = props;

    // -------------------------------------------------------------------------
    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    // -------------------------------------------------------------------------
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // -------------------------------------------------------------------------
    // Docker image — shared by all three Basket Lambda functions.
    // -------------------------------------------------------------------------
    const basketImage = new ecrAssets.DockerImageAsset(this, 'BasketImage', {
      directory: REPO_ROOT,
      file: BASKET_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/Basket/Basket.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const basketCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(basketImage.repository, {
        tagOrDigest: basketImage.imageTag,
        cmd,
      });

    // -------------------------------------------------------------------------
    // 1. basket-shopping-carts-event-publisher
    //    Trigger: DynamoDB Streams on shopping-carts (NEW_IMAGE, CDC — ADR-0005)
    //    On each MODIFY record of Type=Checkout, publishes BasketCheckoutEvent and
    //    deletes the basket item.
    // -------------------------------------------------------------------------
    this.streamPublisher = new lambda.DockerImageFunction(this, 'StreamPublisher', {
      functionName: 'basket-shopping-carts-event-publisher',
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.EventsIntegration.Publisher.ShoppingCartsEventPublisherFunction::FunctionHandler',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'CDC: reads DynamoDB Streams on shopping-carts and publishes BasketCheckoutEvent to EventBridge',
      environment: {
        EventBridge__BusName: eventBus.eventBusName,
      },
    });

    this.streamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(shoppingCartsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
      }),
    );

    // Publisher deletes the basket item after publishing the checkout event.
    shoppingCartsTable.grantWriteData(this.streamPublisher);
    eventBus.grantPutEventsTo(this.streamPublisher);

    // -------------------------------------------------------------------------
    // 2. basket-store-basket  (HTTP API — StoreBasket command)
    //    Trigger: Lambda Function URL (direct HTTP, no API Gateway)
    //    Talks directly to DynamoDB (no cache) — reads coupons to apply discounts,
    //    upserts the cart.
    // -------------------------------------------------------------------------
    this.storeBasket = new lambda.DockerImageFunction(this, 'StoreBasket', {
      functionName: 'basket-store-basket',
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.Functions_StoreBasket_Generated::StoreBasket',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Stores (upserts) a shopping cart in DynamoDB',
    });

    shoppingCartsTable.grantReadWriteData(this.storeBasket);
    couponsTable.grantReadData(this.storeBasket);

    this.storeBasketUrl = this.storeBasket.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // 3. basket-checkout-basket  (HTTP API — CheckoutBasket command)
    //    Talks directly to DynamoDB (no cache).
    //    Writes a Checkout marker to the cart item; the stream publisher picks it
    //    up and publishes BasketCheckoutEvent (CDC pattern, ADR-0005).
    // -------------------------------------------------------------------------
    this.checkoutBasket = new lambda.DockerImageFunction(this, 'CheckoutBasket', {
      functionName: 'basket-checkout-basket',
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.Functions_CheckoutBasket_Generated::CheckoutBasket',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description:
        'Marks a cart as checked out; DynamoDB Streams CDC publishes BasketCheckoutEvent',
    });

    shoppingCartsTable.grantReadWriteData(this.checkoutBasket);
    couponsTable.grantReadData(this.checkoutBasket);

    this.checkoutBasketUrl = this.checkoutBasket.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // 4. basket-merge-basket  (AppSync Invoke — MergeBasket command)
    //    Folds a GUEST# cart into the USER# cart on login (ADR-0016): reads both
    //    carts, writes the merged USER# cart, deletes the GUEST# cart. No Function
    //    URL — invoked directly by the AppSync mergeBasket resolver (Invoke).
    // -------------------------------------------------------------------------
    this.mergeBasket = new lambda.DockerImageFunction(this, 'MergeBasket', {
      functionName: 'basket-merge-basket',
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.Functions_MergeBasket_Generated::MergeBasket',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Merges a guest cart into the user cart on login (invoked by AppSync)',
    });

    shoppingCartsTable.grantReadWriteData(this.mergeBasket);
    couponsTable.grantReadData(this.mergeBasket);
  }
}
