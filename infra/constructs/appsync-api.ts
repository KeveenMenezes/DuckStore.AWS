import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as appsync from 'aws-cdk-lib/aws-appsync';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { Construct } from 'constructs';

const REPO_ROOT = path.join(__dirname, '..', '..');
const RESOLVERS_DIR = path.join(
  REPO_ROOT,
  'src/WebApps/Shopping.Web.SPA.React/graphql/resolvers',
);
const SCHEMA_PATH = path.join(
  REPO_ROOT,
  'src/WebApps/Shopping.Web.SPA.React/graphql/schema.graphql',
);

export interface AppSyncApiProps {
  readonly userPool: cognito.IUserPool;
}

export class AppSyncApi extends Construct {
  public readonly api: appsync.GraphqlApi;

  constructor(scope: Construct, id: string, props: AppSyncApiProps) {
    super(scope, id);

    // -------------------------------------------------------------------------
    // GraphQL API — dual auth:
    //   Primary:   Cognito User Pools (required for mutations + private queries)
    //   Secondary: API_KEY (catalog reads, reviews — public without login)
    // -------------------------------------------------------------------------
    this.api = new appsync.GraphqlApi(this, 'Api', {
      name: 'duckstore-api',
      definition: appsync.Definition.fromFile(SCHEMA_PATH),
      authorizationConfig: {
        defaultAuthorization: {
          authorizationType: appsync.AuthorizationType.USER_POOL,
          userPoolConfig: { userPool: props.userPool },
        },
        additionalAuthorizationModes: [
          {
            authorizationType: appsync.AuthorizationType.API_KEY,
            apiKeyConfig: {
              description: 'Public API key for unauthenticated catalog and review reads',
              expires: cdk.Expiration.after(cdk.Duration.days(365)),
            },
          },
        ],
      },
      xrayEnabled: true,
      logConfig: {
        fieldLogLevel: appsync.FieldLogLevel.ERROR,
        excludeVerboseContent: true,
      },
    });

    this.addDataSources();
  }

  private resolver(
    dataSource: appsync.BaseDataSource,
    id: string,
    typeName: string,
    fieldName: string,
  ) {
    dataSource.createResolver(id, {
      typeName,
      fieldName,
      runtime: appsync.FunctionRuntime.JS_1_0_0,
      code: appsync.Code.fromAsset(
        path.join(RESOLVERS_DIR, `${typeName}.${fieldName}.js`),
      ),
    });
  }

  private addDataSources() {
    const { api } = this;

    // ------------------------------------------------------------------
    // DynamoDB data sources — imported by table name (no CF coupling)
    // ------------------------------------------------------------------
    const productsTable = dynamodb.Table.fromTableName(this, 'ProductsTable', 'products');
    const categoriesTable = dynamodb.Table.fromTableName(this, 'CategoriesTable', 'categories');
    const cartsTable = dynamodb.Table.fromTableName(this, 'CartsTable', 'shopping-carts');
    const couponsTable = dynamodb.Table.fromTableName(this, 'CouponsTable', 'coupons');
    const orderingTable = dynamodb.Table.fromTableName(this, 'OrderingTable', 'ordering');
    const reviewsTable = dynamodb.Table.fromTableName(this, 'ReviewsTable', 'reviews');

    const productsDs = api.addDynamoDbDataSource('ProductsDS', productsTable);
    const categoriesDs = api.addDynamoDbDataSource('CategoriesDS', categoriesTable);
    const cartsDs = api.addDynamoDbDataSource('CartsDS', cartsTable);
    const couponsDs = api.addDynamoDbDataSource('CouponsDS', couponsTable);
    const orderingDs = api.addDynamoDbDataSource('OrderingDS', orderingTable);
    const reviewsDs = api.addDynamoDbDataSource('ReviewsDS', reviewsTable);

    // Explicit grants — addDynamoDbDataSource creates the role but does not auto-grant
    productsTable.grantReadWriteData(productsDs);
    categoriesTable.grantReadData(categoriesDs);
    cartsTable.grantReadWriteData(cartsDs);
    couponsTable.grantReadData(couponsDs);
    orderingTable.grantReadData(orderingDs);
    reviewsTable.grantReadWriteData(reviewsDs);

    // ------------------------------------------------------------------
    // Lambda data sources — imported by function name (no CF coupling)
    // ------------------------------------------------------------------
    const storeBasketFn = lambda.Function.fromFunctionName(
      this,
      'StoreBasketFn',
      'basket-store-basket',
    );
    const checkoutFn = lambda.Function.fromFunctionName(
      this,
      'CheckoutFn',
      'basket-checkout-basket',
    );
    const getOrdersFn = lambda.Function.fromFunctionName(
      this,
      'GetOrdersFn',
      'ordering-get-orders-by-customer',
    );
    const deleteOrderFn = lambda.Function.fromFunctionName(
      this,
      'DeleteOrderFn',
      'ordering-delete-order',
    );

    // addLambdaDataSource automatically grants lambda:InvokeFunction to the DS role
    const storeBasketDs = api.addLambdaDataSource('StoreBasketDS', storeBasketFn);
    const checkoutDs = api.addLambdaDataSource('CheckoutDS', checkoutFn);
    const getOrdersDs = api.addLambdaDataSource('GetOrdersDS', getOrdersFn);
    const deleteOrderDs = api.addLambdaDataSource('DeleteOrderDS', deleteOrderFn);

    // ------------------------------------------------------------------
    // Resolvers — one JS file per (typeName, fieldName) pair
    // ------------------------------------------------------------------

    // Public queries (also accessible via API_KEY — @aws_api_key in schema)
    this.resolver(productsDs, 'ProductsResolver', 'Query', 'products');
    this.resolver(productsDs, 'ProductResolver', 'Query', 'product');
    this.resolver(categoriesDs, 'CategoriesResolver', 'Query', 'categories');
    this.resolver(reviewsDs, 'ReviewsByProductResolver', 'Query', 'reviewsByProduct');

    // Authenticated queries (Cognito default — any group)
    this.resolver(cartsDs, 'BasketResolver', 'Query', 'basket');
    this.resolver(couponsDs, 'CouponForResolver', 'Query', 'couponFor');
    this.resolver(getOrdersDs, 'OrdersByCustomerResolver', 'Query', 'ordersByCustomer');

    // Admin-only queries (Cognito default + group check in resolver)
    this.resolver(orderingDs, 'OrdersResolver', 'Query', 'orders');
    this.resolver(orderingDs, 'OrdersByNameResolver', 'Query', 'ordersByName');

    // Authenticated mutations — Customer group
    this.resolver(storeBasketDs, 'StoreBasketResolver', 'Mutation', 'storeBasket');
    this.resolver(checkoutDs, 'CheckoutBasketResolver', 'Mutation', 'checkoutBasket');
    this.resolver(cartsDs, 'DeleteBasketResolver', 'Mutation', 'deleteBasket');
    this.resolver(reviewsDs, 'CreateReviewResolver', 'Mutation', 'createReview');

    // Admin/Seller mutations (group check in resolver)
    this.resolver(productsDs, 'CreateProductResolver', 'Mutation', 'createProduct');
    this.resolver(productsDs, 'UpdateProductResolver', 'Mutation', 'updateProduct');
    this.resolver(productsDs, 'DeleteProductResolver', 'Mutation', 'deleteProduct');

    // Admin-only mutations (group check in resolver)
    this.resolver(deleteOrderDs, 'DeleteOrderResolver', 'Mutation', 'deleteOrder');
  }
}
