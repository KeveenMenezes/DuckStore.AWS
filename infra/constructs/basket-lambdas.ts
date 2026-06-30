import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import * as events from 'aws-cdk-lib/aws-events';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as dax from 'aws-cdk-lib/aws-dax';
import { Construct } from 'constructs';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const BASKET_DOCKERFILE = 'src/Services/Basket/Basket.Function/Dockerfile';

export interface BasketLambdasProps {
  readonly shoppingCartsTable: dynamodb.Table;
  readonly couponsTable: dynamodb.Table;
  /** VPC used by DAX cluster and the HTTP API Lambda functions. */
  readonly vpc: ec2.IVpc;
}

export class BasketLambdas extends Construct {
  public readonly streamPublisher: lambda.Function;
  public readonly storeBasket: lambda.Function;
  public readonly checkoutBasket: lambda.Function;
  public readonly storeBasketUrl: lambda.FunctionUrl;
  public readonly checkoutBasketUrl: lambda.FunctionUrl;

  constructor(scope: Construct, id: string, props: BasketLambdasProps) {
    super(scope, id);

    const { shoppingCartsTable, couponsTable, vpc } = props;

    // -------------------------------------------------------------------------
    // EventBridge bus — created by CatalogStack; imported here by name (ADR-0004).
    // -------------------------------------------------------------------------
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // -------------------------------------------------------------------------
    // DAX cluster — provides transparent read/write caching for the HTTP API
    // Lambdas (basket-store-basket, basket-checkout-basket). The stream publisher
    // runs outside the VPC and uses the standard DynamoDB client directly.
    // -------------------------------------------------------------------------
    const daxRole = new iam.Role(this, 'DaxRole', {
      assumedBy: new iam.ServicePrincipal('dax.amazonaws.com'),
      description: 'Allows the DAX cluster to read/write DynamoDB on behalf of Basket Lambdas',
    });
    shoppingCartsTable.grantReadWriteData(daxRole);
    couponsTable.grantReadWriteData(daxRole);

    const daxSg = new ec2.SecurityGroup(this, 'DaxSG', {
      vpc,
      description: 'Controls inbound access to the Basket DAX cluster',
      allowAllOutbound: false,
    });

    const lambdaSg = new ec2.SecurityGroup(this, 'LambdaSG', {
      vpc,
      description: 'Basket HTTP API Lambda functions (store-basket, checkout-basket)',
      allowAllOutbound: true,
    });

    // DAX unencrypted port (8111); sufficient for an isolated dev account.
    daxSg.addIngressRule(lambdaSg, ec2.Port.tcp(8111), 'DAX from Basket Lambda');

    const subnetIds = vpc.selectSubnets({ subnetType: ec2.SubnetType.PUBLIC }).subnetIds;

    const daxSubnetGroup = new dax.CfnSubnetGroup(this, 'DaxSubnetGroup', {
      subnetGroupName: 'basket-dax-subnet-group',
      description: 'Subnet group for Basket DAX cluster',
      subnetIds,
    });

    const daxCluster = new dax.CfnCluster(this, 'DaxCluster', {
      clusterName: 'basket-dax',
      // dax.t3.small is the smallest node type — appropriate for a dev account.
      nodeType: 'dax.t3.small',
      replicationFactor: 1,
      iamRoleArn: daxRole.roleArn,
      subnetGroupName: daxSubnetGroup.ref,
      securityGroupIds: [daxSg.securityGroupId],
      sseSpecification: { sseEnabled: false },
    });
    daxCluster.addDependency(daxSubnetGroup);

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
    //    Runs OUTSIDE the VPC: only needs public DynamoDB and EventBridge endpoints.
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
    //    Runs IN the VPC so it can reach the DAX cluster.
    //    DOTNET_ENVIRONMENT=Production → BasketStorageExtensions uses DAX path.
    // -------------------------------------------------------------------------
    const daxEndpoint = daxCluster.attrClusterDiscoveryEndpoint;

    this.storeBasket = new lambda.DockerImageFunction(this, 'StoreBasket', {
      functionName: 'basket-store-basket',
      architecture: DOTNET_ARCH,
      code: basketCode([
        'Basket.Function::Basket.Function.Functions_StoreBasket_Generated::StoreBasket',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Stores (upserts) a shopping cart via DAX-backed DynamoDB',
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      // Lambda in a public subnet has no internet access (no public IP assigned).
      // These functions only need to reach the DAX cluster (private, same VPC),
      // so no internet is required. Flag is required by CDK to acknowledge this.
      allowPublicSubnet: true,
      securityGroups: [lambdaSg],
      environment: {
        DOTNET_ENVIRONMENT: 'Production',
        Dax__Endpoint: daxEndpoint,
        Dax__Port: '8111',
      },
    });

    this.storeBasket.addToRolePolicy(
      new iam.PolicyStatement({
        actions: [
          'dax:GetItem', 'dax:PutItem', 'dax:UpdateItem', 'dax:DeleteItem',
          'dax:Query', 'dax:Scan', 'dax:BatchGetItem', 'dax:BatchWriteItem',
        ],
        resources: [daxCluster.attrArn],
      }),
    );

    this.storeBasketUrl = this.storeBasket.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // -------------------------------------------------------------------------
    // 3. basket-checkout-basket  (HTTP API — CheckoutBasket command)
    //    Same VPC + DAX setup as store-basket.
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
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      allowPublicSubnet: true,
      securityGroups: [lambdaSg],
      environment: {
        DOTNET_ENVIRONMENT: 'Production',
        Dax__Endpoint: daxEndpoint,
        Dax__Port: '8111',
      },
    });

    this.checkoutBasket.addToRolePolicy(
      new iam.PolicyStatement({
        actions: [
          'dax:GetItem', 'dax:PutItem', 'dax:UpdateItem', 'dax:DeleteItem',
          'dax:Query', 'dax:Scan', 'dax:BatchGetItem', 'dax:BatchWriteItem',
        ],
        resources: [daxCluster.attrArn],
      }),
    );

    this.checkoutBasketUrl = this.checkoutBasket.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });
  }
}
