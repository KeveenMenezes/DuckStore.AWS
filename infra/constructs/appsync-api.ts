import * as path from 'path';
import { readFileSync } from 'fs';
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
    const filePath = path.join(RESOLVERS_DIR, `${typeName}.${fieldName}.js`);
    dataSource.createResolver(id, {
      typeName,
      fieldName,
      runtime: appsync.FunctionRuntime.JS_1_0_0,
      code: appsync.Code.fromInline(readFileSync(filePath, 'utf-8')),
    });
  }

  // Pipeline resolver (ADR-0029) — this repo's first: every other resolver here is a single-step
  // unit resolver against one data source. `steps` are the intermediate AppSync Functions (each its
  // own request/response, e.g. `Mutation.createReview.checkExisting.js`), run in order and sharing
  // `ctx.stash`/`ctx.prev.result`; the top-level `${typeName}.${fieldName}.js` file is just the
  // pipeline's own pass-through request/response (returns `ctx.prev.result`).
  private pipelineResolver(
    dataSource: appsync.BaseDataSource,
    id: string,
    typeName: string,
    fieldName: string,
    steps: string[],
  ) {
    const runtime = appsync.FunctionRuntime.JS_1_0_0;
    const readResolverCode = (fileName: string) =>
      appsync.Code.fromInline(readFileSync(path.join(RESOLVERS_DIR, fileName), 'utf-8'));

    const functions = steps.map(
      (fileName, index) =>
        new appsync.AppsyncFunction(this, `${id}Fn${index}`, {
          api: this.api,
          dataSource,
          name: `${id}Fn${index}`,
          runtime,
          code: readResolverCode(fileName),
        }),
    );

    this.api.createResolver(id, {
      typeName,
      fieldName,
      pipelineConfig: functions,
      runtime,
      code: readResolverCode(`${typeName}.${fieldName}.js`),
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
    // Pricing tables (ADR-0026) — nominal price and the product-discounts projection are both
    // simple key lookups, read via direct DynamoDB data sources like everything else here.
    const pricesTable = dynamodb.Table.fromTableName(this, 'PricesTable', 'prices');
    const productDiscountsTable = dynamodb.Table.fromTableName(
      this,
      'ProductDiscountsTable',
      'product-discounts',
    );
    // fromTableAttributes + grantIndexPermissions is required (not fromTableName) so that
    // grantReadWriteData below also covers the GSI1 ARN used by ordersByCustomer/reviewsByProduct.
    const orderingTable = dynamodb.Table.fromTableAttributes(this, 'OrderingTable', {
      tableName: 'ordering',
      grantIndexPermissions: true,
    });
    const reviewsTable = dynamodb.Table.fromTableAttributes(this, 'ReviewsTable', {
      tableName: 'reviews',
      grantIndexPermissions: true,
    });
    const userProfilesTable = dynamodb.Table.fromTableName(this, 'UserProfilesTable', 'user-profiles');
    // CatalogView (ADR-0030, supersedes ADR-0027) — product read/search is DynamoDB-backed again;
    // products/product are Direct resolvers, not Lambda. fromTableAttributes +
    // grantIndexPermissions is required (not fromTableName) so grantReadData below also covers
    // the GSI1 ARN used by the rating-sorted browse path in Query.products.js.
    const catalogViewProductsTable = dynamodb.Table.fromTableAttributes(this, 'CatalogViewProductsTable', {
      tableName: 'catalogview-products',
      grantIndexPermissions: true,
    });

    const productsDs = api.addDynamoDbDataSource('ProductsDS', productsTable);
    const categoriesDs = api.addDynamoDbDataSource('CategoriesDS', categoriesTable);
    const cartsDs = api.addDynamoDbDataSource('CartsDS', cartsTable);
    const pricesDs = api.addDynamoDbDataSource('PricesDS', pricesTable);
    const productDiscountsDs = api.addDynamoDbDataSource('ProductDiscountsDS', productDiscountsTable);
    const orderingDs = api.addDynamoDbDataSource('OrderingDS', orderingTable);
    const reviewsDs = api.addDynamoDbDataSource('ReviewsDS', reviewsTable);
    const userProfilesDs = api.addDynamoDbDataSource('UserProfilesDS', userProfilesTable);
    const catalogViewProductsDs = api.addDynamoDbDataSource(
      'CatalogViewProductsDS',
      catalogViewProductsTable,
    );

    // Explicit grants — addDynamoDbDataSource creates the role but does not auto-grant
    productsTable.grantReadWriteData(productsDs);
    categoriesTable.grantReadData(categoriesDs);
    cartsTable.grantReadWriteData(cartsDs);
    pricesTable.grantReadData(pricesDs);
    productDiscountsTable.grantReadData(productDiscountsDs);
    // Read for ordersByCustomer/orders/ordersByName queries; write for the deleteOrder DeleteItem
    // resolver (ADR-0009 — both are direct DynamoDB, no Lambda).
    orderingTable.grantReadWriteData(orderingDs);
    reviewsTable.grantReadWriteData(reviewsDs);
    userProfilesTable.grantReadWriteData(userProfilesDs);
    catalogViewProductsTable.grantReadData(catalogViewProductsDs);

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
    const mergeBasketFn = lambda.Function.fromFunctionName(
      this,
      'MergeBasketFn',
      'basket-merge-basket',
    );
    const getProfileFn = lambda.Function.fromFunctionName(
      this,
      'GetProfileFn',
      'user-get-profile',
    );
    const setNominalPriceFn = lambda.Function.fromFunctionName(
      this,
      'SetNominalPriceFn',
      'pricing-set-nominal-price',
    );
    const createCampaignFn = lambda.Function.fromFunctionName(
      this,
      'CreateCampaignFn',
      'pricing-create-campaign',
    );
    const endCampaignFn = lambda.Function.fromFunctionName(
      this,
      'EndCampaignFn',
      'pricing-end-campaign',
    );
    const getInstallmentPlanFn = lambda.Function.fromFunctionName(
      this,
      'GetInstallmentPlanFn',
      'pricing-get-installment-plan',
    );
    const getBasketInstallmentPlanFn = lambda.Function.fromFunctionName(
      this,
      'GetBasketInstallmentPlanFn',
      'pricing-get-basket-installment-plan',
    );
    const setGatewayCostFn = lambda.Function.fromFunctionName(
      this,
      'SetGatewayCostFn',
      'pricing-set-gateway-cost',
    );
    // addLambdaDataSource automatically grants lambda:InvokeFunction to the DS role
    const storeBasketDs = api.addLambdaDataSource('StoreBasketDS', storeBasketFn);
    const checkoutDs = api.addLambdaDataSource('CheckoutDS', checkoutFn);
    const mergeBasketDs = api.addLambdaDataSource('MergeBasketDS', mergeBasketFn);
    const getProfileDs = api.addLambdaDataSource('GetProfileDS', getProfileFn);
    const setNominalPriceDs = api.addLambdaDataSource('SetNominalPriceDS', setNominalPriceFn);
    const createCampaignDs = api.addLambdaDataSource('CreateCampaignDS', createCampaignFn);
    const endCampaignDs = api.addLambdaDataSource('EndCampaignDS', endCampaignFn);
    const getInstallmentPlanDs = api.addLambdaDataSource('GetInstallmentPlanDS', getInstallmentPlanFn);
    const getBasketInstallmentPlanDs = api.addLambdaDataSource(
      'GetBasketInstallmentPlanDS',
      getBasketInstallmentPlanFn,
    );
    const setGatewayCostDs = api.addLambdaDataSource('SetGatewayCostDS', setGatewayCostFn);

    // ------------------------------------------------------------------
    // Resolvers — one JS file per (typeName, fieldName) pair
    // ------------------------------------------------------------------

    // Public queries (also accessible via API_KEY — @aws_api_key in schema)
    // Product read/search — CatalogView Direct DynamoDB resolvers (ADR-0030), not Catalog's
    // products table.
    this.resolver(catalogViewProductsDs, 'ProductsResolver', 'Query', 'products');
    this.resolver(catalogViewProductsDs, 'ProductResolver', 'Query', 'product');
    this.resolver(categoriesDs, 'CategoriesResolver', 'Query', 'categories');
    this.resolver(reviewsDs, 'ReviewsByProductResolver', 'Query', 'reviewsByProduct');
    this.resolver(pricesDs, 'NominalPriceForResolver', 'Query', 'nominalPriceFor');
    // Direct DynamoDB GetItem + read-time expiry check (ADR-0026) — replaces couponFor.
    this.resolver(productDiscountsDs, 'CurrentDiscountForProductResolver', 'Query', 'currentDiscountForProduct');
    this.resolver(getInstallmentPlanDs, 'InstallmentPlanForResolver', 'Query', 'installmentPlanFor');
    this.resolver(getBasketInstallmentPlanDs, 'BasketInstallmentPlanResolver', 'Query', 'basketInstallmentPlan');

    // Authenticated queries (Cognito default — any group)
    this.resolver(cartsDs, 'BasketResolver', 'Query', 'basket');
    // Direct DynamoDB GSI1 query — scoped to the caller's Cognito sub in the resolver (ADR-0009).
    this.resolver(orderingDs, 'OrdersByCustomerResolver', 'Query', 'ordersByCustomer');

    // Admin-only queries (Cognito default + group check in resolver)
    this.resolver(orderingDs, 'OrdersResolver', 'Query', 'orders');
    this.resolver(orderingDs, 'OrdersByNameResolver', 'Query', 'ordersByName');

    // User profile — Cognito-only. myProfile is a Lambda resolver (lazy provisioning),
    // updateProfile is a direct DynamoDB UpdateItem resolver (ADR-0017).
    this.resolver(getProfileDs, 'MyProfileResolver', 'Query', 'myProfile');
    this.resolver(userProfilesDs, 'UpdateProfileResolver', 'Mutation', 'updateProfile');

    // Basket mutations — storeBasket/deleteBasket allow Cognito OR API_KEY (guest via BFF);
    // checkoutBasket/mergeBasket are Cognito-only (see ADR-0016).
    this.resolver(storeBasketDs, 'StoreBasketResolver', 'Mutation', 'storeBasket');
    this.resolver(checkoutDs, 'CheckoutBasketResolver', 'Mutation', 'checkoutBasket');
    this.resolver(mergeBasketDs, 'MergeBasketResolver', 'Mutation', 'mergeBasket');
    this.resolver(cartsDs, 'DeleteBasketResolver', 'Mutation', 'deleteBasket');
    this.pipelineResolver(reviewsDs, 'CreateReviewResolver', 'Mutation', 'createReview', [
      'Mutation.createReview.checkExisting.js',
      'Mutation.createReview.upsert.js',
    ]);

    // Admin/Seller mutations (group check in resolver)
    this.resolver(productsDs, 'CreateProductResolver', 'Mutation', 'createProduct');
    this.resolver(productsDs, 'UpdateProductResolver', 'Mutation', 'updateProduct');
    this.resolver(productsDs, 'DeleteProductResolver', 'Mutation', 'deleteProduct');

    // Admin-only mutations (group check in resolver)
    this.resolver(orderingDs, 'DeleteOrderResolver', 'Mutation', 'deleteOrder');

    // Admin/Seller mutations — Pricing (ADR-0026). setNominalPrice is decoupled from
    // createProduct/updateProduct (see Mutation.createProduct.js); campaigns are Admin-only.
    this.resolver(setNominalPriceDs, 'SetNominalPriceResolver', 'Mutation', 'setNominalPrice');
    this.resolver(createCampaignDs, 'CreateCampaignResolver', 'Mutation', 'createCampaign');
    this.resolver(endCampaignDs, 'EndCampaignResolver', 'Mutation', 'endCampaign');

    // Admin-only mutation — configures a payment-gateway provider's cost table (ADR-0028).
    this.resolver(setGatewayCostDs, 'SetGatewayCostResolver', 'Mutation', 'setGatewayCost');
  }
}
