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
}

export class BasketLambdas extends Construct {
  public readonly streamPublisher: lambda.Function;
  public readonly checkoutBasket: lambda.Function;
  public readonly mergeBasket: lambda.Function;
  public readonly checkoutBasketUrl: lambda.FunctionUrl;

  constructor(scope: Construct, id: string, props: BasketLambdasProps) {
    super(scope, id);

    const { shoppingCartsTable } = props;

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
    //    Rule-based publisher (ADR-0019): on each MODIFY record of Type=Checkout,
    //    CheckoutedRule publishes BasketCheckoutEvent. The basket item's deletion
    //    happens synchronously in CheckoutBasketCommandHandler, not here.
    // -------------------------------------------------------------------------
    this.streamPublisher = new lambda.DockerImageFunction(this, 'StreamPublisher', {
      functionName: 'basket-shopping-carts-event-publisher',
      // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.Functions_ShoppingCartStreamPublisher_Generated::ShoppingCartStreamPublisher',
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

    eventBus.grantPutEventsTo(this.streamPublisher);

    // Note: basket-store-basket (StoreBasket command) is gone — storeBasket is now an AppSync
    // direct DynamoDB PutItem resolver (ADR-0009); see infra/constructs/appsync-api.ts and
    // graphql/resolvers/basket/mutations/Mutation.storeBasket.js.

    // -------------------------------------------------------------------------
    // 2. basket-checkout-basket  (HTTP API — CheckoutBasket command)
    //    Talks directly to DynamoDB (no cache).
    //    Writes a Checkout marker to the cart item; the stream publisher picks it
    //    up and publishes BasketCheckoutEvent (CDC pattern, ADR-0005).
    // -------------------------------------------------------------------------
    this.checkoutBasket = new lambda.DockerImageFunction(this, 'CheckoutBasket', {
      functionName: 'basket-checkout-basket',
      // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
      tracing: lambda.Tracing.ACTIVE,
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

    this.checkoutBasketUrl = this.checkoutBasket.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // 3. basket-merge-basket  (AppSync Invoke — MergeBasket command)
    //    Folds a GUEST# cart into the USER# cart on login (ADR-0016): reads both
    //    carts, writes the merged USER# cart, deletes the GUEST# cart. No Function
    //    URL — invoked directly by the AppSync mergeBasket resolver (Invoke).
    // -------------------------------------------------------------------------
    this.mergeBasket = new lambda.DockerImageFunction(this, 'MergeBasket', {
      functionName: 'basket-merge-basket',
      // X-Ray active tracing so the trace AppSync starts continues into the Lambda (ADR-0022).
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.Functions_MergeBasket_Generated::MergeBasket',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Merges a guest cart into the user cart on login (invoked by AppSync)',
    });

    shoppingCartsTable.grantReadWriteData(this.mergeBasket);
  }
}
