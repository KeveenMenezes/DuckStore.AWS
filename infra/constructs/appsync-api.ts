import * as path from 'path';
import { readFileSync } from 'fs';
import * as cdk from 'aws-cdk-lib';
import * as appsync from 'aws-cdk-lib/aws-appsync';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as sfn from 'aws-cdk-lib/aws-stepfunctions';
import { Construct } from 'constructs';
import { REWARD_CURRENCY_PER_UNIT, REWARD_EXPIRY_DAYS, REWARD_POINTS_PER_UNIT } from './reward-config';

// The GraphQL contract lives at the repo root (ADR-0033) — shared infrastructure, not
// SPA code: the CDK deploys it to AppSync, the SPA's local yoga backend executes it in
// dev, and every client (React SPA, Blazor admin) programs against it.
const REPO_ROOT = path.join(__dirname, '..', '..');
const RESOLVERS_DIR = path.join(REPO_ROOT, 'graphql/resolvers');
const SCHEMA_PATH = path.join(REPO_ROOT, 'graphql/schema.graphql');

export interface AppSyncApiProps {
  // Primary auth — customers, React SPA.
  readonly shoppingUserPool: cognito.IUserPool;
  // Additional auth — staff (Admin/Seller), Blazor management app.
  readonly managementUserPool: cognito.IUserPool;
  // Express saga behind createProductWithPrice (ADR-0032), invoked synchronously via
  // an HTTP datasource calling states:StartSyncExecution.
  readonly productCreateSaga: sfn.IStateMachine;
}

export class AppSyncApi extends Construct {
  public readonly api: appsync.GraphqlApi;
  private readonly productCreateSaga: sfn.IStateMachine;

  constructor(scope: Construct, id: string, props: AppSyncApiProps) {
    super(scope, id);
    this.productCreateSaga = props.productCreateSaga;

    // GraphQL API — three auth modes:
    //   Default:    Shopping Cognito user pool (customers, required for mutations +
    //               private queries)
    //   Additional: Management Cognito user pool (staff — Admin/Seller — tokens are
    //               accepted the same way; @aws_cognito_user_pools in the schema means
    //               "any configured Cognito auth mode", and resolvers authorize purely
    //               via ctx.identity.groups/sub, so no pool-specific resolver logic
    //               is needed)
    //   Additional: API_KEY (catalog reads, reviews — public without login)
    this.api = new appsync.GraphqlApi(this, 'Api', {
      name: 'duckstore-api',
      definition: appsync.Definition.fromFile(SCHEMA_PATH),
      authorizationConfig: {
        defaultAuthorization: {
          authorizationType: appsync.AuthorizationType.USER_POOL,
          userPoolConfig: { userPool: props.shoppingUserPool },
        },
        additionalAuthorizationModes: [
          {
            authorizationType: appsync.AuthorizationType.USER_POOL,
            userPoolConfig: { userPool: props.managementUserPool },
          },
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

  // Resolver files live under `graphql/resolvers/<domain>/<queries|mutations>/<Type>.<field>.js`
  // (one folder per bounded context, split by operation kind — see the "Resolver organization"
  // note above `addDataSources()`). `typeName` ('Query'/'Mutation') maps 1:1 to the subfolder.
  private resolverPath(domain: string, typeName: string, fileName: string): string {
    const subfolder = typeName === 'Query' ? 'queries' : 'mutations';
    return path.join(RESOLVERS_DIR, domain, subfolder, fileName);
  }

  // `replacements` substitutes synth-time-only values (e.g. a state machine ARN) into the
  // resolver source before inlining — APPSYNC_JS has no env vars, so placeholders like
  // __STATE_MACHINE_ARN__ in the .js file are the only way to inject deploy-time identifiers.
  private resolver(
    dataSource: appsync.BaseDataSource,
    id: string,
    typeName: string,
    fieldName: string,
    domain: string,
    replacements?: Record<string, string>,
  ) {
    const filePath = this.resolverPath(domain, typeName, `${typeName}.${fieldName}.js`);
    let code = readFileSync(filePath, 'utf-8');
    for (const [placeholder, value] of Object.entries(replacements ?? {})) {
      code = code.split(placeholder).join(value);
    }
    dataSource.createResolver(id, {
      typeName,
      fieldName,
      runtime: appsync.FunctionRuntime.JS_1_0_0,
      code: appsync.Code.fromInline(code),
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
    domain: string,
    steps: string[],
  ) {
    const runtime = appsync.FunctionRuntime.JS_1_0_0;
    const readResolverCode = (fileName: string) =>
      appsync.Code.fromInline(readFileSync(this.resolverPath(domain, typeName, fileName), 'utf-8'));

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

  // Resolver organization: `graphql/resolvers/` is split by domain (basket, products, categories,
  // pricing, orders, reviews, user — matching the bounded contexts in `src/Services/*`), and each
  // domain folder is split into `queries/`/`mutations/`. The `domain` string passed to
  // `this.resolver(...)`/`this.pipelineResolver(...)` below is what points at that folder — keep
  // resolvers grouped in the same order here so the two stay easy to cross-reference.
  private addDataSources() {
    const { api } = this;

    // DynamoDB data sources — imported by table name (no CF coupling)
    const productsTable = dynamodb.Table.fromTableName(this, 'ProductsTable', 'products');
    const categoriesTable = dynamodb.Table.fromTableName(this, 'CategoriesTable', 'categories');
    const cartsTable = dynamodb.Table.fromTableName(this, 'CartsTable', 'shopping-carts');
    // Pricing's prices table (ADR-0026) — nominal price is a simple key lookup, read via a
    // direct DynamoDB data source like everything else here.
    const pricesTable = dynamodb.Table.fromTableName(this, 'PricesTable', 'prices');
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
    // Pricing's gateway-costs table (ADR-0028) — setGatewayCost is now a direct UpdateItem
    // resolver (ADR-0009), no Lambda.
    const gatewayCostsTable = dynamodb.Table.fromTableName(this, 'GatewayCostsTable', 'gateway-costs');
    // Pricing's campaigns table (ADR-0026) — listing is a plain Scan with no business logic
    // (ADR-0009), unlike createCampaign/endCampaign which stay Lambda (TransactWriteItems fan-out).
    const campaignsTable = dynamodb.Table.fromTableName(this, 'CampaignsTable', 'campaigns');
    // CatalogView (ADR-0030, supersedes ADR-0027) — product read/search is DynamoDB-backed again;
    // products/product are Direct resolvers, not Lambda. fromTableAttributes +
    // grantIndexPermissions is required (not fromTableName) so grantReadData below also covers
    // the GSI1 ARN used by the rating-sorted browse path in Query.products.js.
    const catalogViewProductsTable = dynamodb.Table.fromTableAttributes(this, 'CatalogViewProductsTable', {
      tableName: 'catalogview-products',
      grantIndexPermissions: true,
    });
    // Challenges (ADR-0045). GSI1 is sparse: only the PUBLIC item of each question carries
    // GSI1PK/GSI1SK, so grantIndexPermissions is required (not fromTableName) the same way it is
    // for reviews/ordering/catalogview-products above.
    const challengesTable = dynamodb.Table.fromTableAttributes(this, 'ChallengesTable', {
      tableName: 'challenges',
      grantIndexPermissions: true,
    });
    // No GSI — myChallengeProgress queries the base table by OwnerId alone (ADR-0045 §4/§5).
    const challengeProgressTable = dynamodb.Table.fromTableName(
      this, 'ChallengeProgressTable', 'challenge-progress',
    );
    // Customer-scoped reward (ADR-0046 §4) — no GSI, myRewards queries the base table by OwnerId.
    const customerDiscountsTable = dynamodb.Table.fromTableName(
      this, 'CustomerDiscountsTable', 'customer-discounts',
    );

    const productsDs = api.addDynamoDbDataSource('ProductsDS', productsTable);
    const categoriesDs = api.addDynamoDbDataSource('CategoriesDS', categoriesTable);
    const cartsDs = api.addDynamoDbDataSource('CartsDS', cartsTable);
    const pricesDs = api.addDynamoDbDataSource('PricesDS', pricesTable);
    const orderingDs = api.addDynamoDbDataSource('OrderingDS', orderingTable);
    const reviewsDs = api.addDynamoDbDataSource('ReviewsDS', reviewsTable);
    const userProfilesDs = api.addDynamoDbDataSource('UserProfilesDS', userProfilesTable);
    const gatewayCostsDs = api.addDynamoDbDataSource('GatewayCostsDS', gatewayCostsTable);
    const campaignsDs = api.addDynamoDbDataSource('CampaignsDS', campaignsTable);
    const catalogViewProductsDs = api.addDynamoDbDataSource(
      'CatalogViewProductsDS',
      catalogViewProductsTable,
    );
    const challengesDs = api.addDynamoDbDataSource('ChallengesDS', challengesTable);
    const challengeProgressDs = api.addDynamoDbDataSource('ChallengeProgressDS', challengeProgressTable);
    const customerDiscountsDs = api.addDynamoDbDataSource('CustomerDiscountsDS', customerDiscountsTable);
    // NONE (local) data source — rewardConversion has no backend call, values are baked in below.
    const rewardConversionDs = api.addNoneDataSource('RewardConversionDS');

    // Explicit grants — addDynamoDbDataSource creates the role but does not auto-grant
    productsTable.grantReadWriteData(productsDs);
    categoriesTable.grantReadData(categoriesDs);
    cartsTable.grantReadWriteData(cartsDs);
    // Write needed too: setNominalPrice is now a direct UpdateItem resolver (ADR-0009).
    pricesTable.grantReadWriteData(pricesDs);
    // Read for ordersByCustomer/orders/ordersByName queries; write for the deleteOrder DeleteItem
    // resolver (ADR-0009 — both are direct DynamoDB, no Lambda).
    orderingTable.grantReadWriteData(orderingDs);
    reviewsTable.grantReadWriteData(reviewsDs);
    userProfilesTable.grantReadWriteData(userProfilesDs);
    gatewayCostsTable.grantReadWriteData(gatewayCostsDs);
    campaignsTable.grantReadData(campaignsDs);
    catalogViewProductsTable.grantReadData(catalogViewProductsDs);
    challengesTable.grantReadData(challengesDs);
    challengeProgressTable.grantReadData(challengeProgressDs);
    customerDiscountsTable.grantReadData(customerDiscountsDs);

    // Lambda data sources — imported by function name (no CF coupling)
    const checkoutFn = lambda.Function.fromFunctionName(this, 'CheckoutFn', 'basket-checkout-basket');
    const mergeBasketFn = lambda.Function.fromFunctionName(this, 'MergeBasketFn', 'basket-merge-basket');
    const createCampaignFn = lambda.Function.fromFunctionName(
      this, 'CreateCampaignFn', 'pricing-create-campaign',
    );
    const endCampaignFn = lambda.Function.fromFunctionName(this, 'EndCampaignFn', 'pricing-end-campaign');
    const getInstallmentPlanFn = lambda.Function.fromFunctionName(
      this, 'GetInstallmentPlanFn', 'pricing-get-installment-plan',
    );
    const getBasketInstallmentPlanFn = lambda.Function.fromFunctionName(
      this, 'GetBasketInstallmentPlanFn', 'pricing-get-basket-installment-plan',
    );
    const presignImageUploadFn = lambda.Function.fromFunctionName(
      this, 'PresignImageUploadFn', 'product-images-presign',
    );
    const submitChallengeAnswerFn = lambda.Function.fromFunctionName(
      this, 'SubmitChallengeAnswerFn', 'challenges-submit-answer',
    );
    const revealChallengeHintFn = lambda.Function.fromFunctionName(
      this, 'RevealChallengeHintFn', 'challenges-reveal-hint',
    );
    const redeemChallengePointsFn = lambda.Function.fromFunctionName(
      this, 'RedeemChallengePointsFn', 'challenges-redeem-points',
    );
    // addLambdaDataSource automatically grants lambda:InvokeFunction to the DS role
    const checkoutDs = api.addLambdaDataSource('CheckoutDS', checkoutFn);
    const mergeBasketDs = api.addLambdaDataSource('MergeBasketDS', mergeBasketFn);
    const createCampaignDs = api.addLambdaDataSource('CreateCampaignDS', createCampaignFn);
    const endCampaignDs = api.addLambdaDataSource('EndCampaignDS', endCampaignFn);
    const getInstallmentPlanDs = api.addLambdaDataSource('GetInstallmentPlanDS', getInstallmentPlanFn);
    const getBasketInstallmentPlanDs = api.addLambdaDataSource(
      'GetBasketInstallmentPlanDS',
      getBasketInstallmentPlanFn,
    );
    const presignImageUploadDs = api.addLambdaDataSource(
      'PresignImageUploadDS',
      presignImageUploadFn,
    );
    const submitChallengeAnswerDs = api.addLambdaDataSource(
      'SubmitChallengeAnswerDS',
      submitChallengeAnswerFn,
    );
    const revealChallengeHintDs = api.addLambdaDataSource(
      'RevealChallengeHintDS',
      revealChallengeHintFn,
    );
    const redeemChallengePointsDs = api.addLambdaDataSource(
      'RedeemChallengePointsDS',
      redeemChallengePointsFn,
    );

    // HTTP data source — Step Functions StartSyncExecution (ADR-0032)
    // The sync-states.<region> endpoint is the dedicated StartSyncExecution endpoint —
    // the regular states.<region> endpoint rejects that action.
    const region = cdk.Stack.of(this).region;
    const sfnDs = api.addHttpDataSource('SfnDS', `https://sync-states.${region}.amazonaws.com`, {
      authorizationConfig: {
        signingRegion: region,
        signingServiceName: 'states',
      },
    });
    this.productCreateSaga.grantStartSyncExecution(sfnDs);

    // Resolvers — one JS file per (typeName, fieldName) pair

    // Public queries (also accessible via API_KEY — @aws_api_key in schema)
    // Product read/search — CatalogView Direct DynamoDB resolvers (ADR-0030), not Catalog's
    // products table.
    this.resolver(catalogViewProductsDs, 'ProductsResolver', 'Query', 'products', 'products');
    this.resolver(catalogViewProductsDs, 'ProductResolver', 'Query', 'product', 'products');
    this.resolver(categoriesDs, 'CategoriesResolver', 'Query', 'categories', 'categories');
    this.resolver(reviewsDs, 'ReviewsByProductResolver', 'Query', 'reviewsByProduct', 'reviews');
    this.resolver(pricesDs, 'NominalPriceForResolver', 'Query', 'nominalPriceFor', 'pricing');
    this.resolver(getInstallmentPlanDs, 'InstallmentPlanForResolver', 'Query', 'installmentPlanFor', 'pricing');
    this.resolver(
      getBasketInstallmentPlanDs, 'BasketInstallmentPlanResolver', 'Query', 'basketInstallmentPlan', 'pricing',
    );
    // Challenges (ADR-0045) — direct DynamoDB resolvers; GSI1 is sparse so the ANSWER item can
    // never surface through either field.
    this.resolver(challengesDs, 'ChallengesResolver', 'Query', 'challenges', 'challenges');
    this.resolver(challengesDs, 'ChallengeResolver', 'Query', 'challenge', 'challenges');

    // Authenticated queries (Cognito default — any group)
    this.resolver(cartsDs, 'BasketResolver', 'Query', 'basket', 'basket');
    // Direct DynamoDB resolver — Cognito only; guests play but don't score (ADR-0045 §7).
    this.resolver(challengeProgressDs, 'MyChallengeProgressResolver', 'Query', 'myChallengeProgress', 'challenges');
    // Direct DynamoDB GSI1 query — scoped to the caller's Cognito sub in the resolver (ADR-0009).
    this.resolver(orderingDs, 'OrdersByCustomerResolver', 'Query', 'ordersByCustomer', 'orders');
    // Direct DynamoDB resolver — Cognito only; Query on OwnerId filtered to Status=Issued and not
    // expired (ADR-0046 §4).
    this.resolver(customerDiscountsDs, 'MyRewardsResolver', 'Query', 'myRewards', 'pricing');
    // NONE resolver — Cognito only; values baked in at synth time (ADR-0046 §8).
    this.resolver(
      rewardConversionDs, 'RewardConversionResolver', 'Query', 'rewardConversion', 'pricing',
      {
        __REWARD_POINTS_PER_UNIT__: REWARD_POINTS_PER_UNIT.toString(),
        __REWARD_CURRENCY_PER_UNIT__: REWARD_CURRENCY_PER_UNIT.toString(),
        __REWARD_EXPIRY_DAYS__: REWARD_EXPIRY_DAYS.toString(),
      },
    );

    // Admin-only queries (Cognito default + group check in resolver)
    this.resolver(orderingDs, 'OrdersResolver', 'Query', 'orders', 'orders');
    this.resolver(orderingDs, 'OrdersByNameResolver', 'Query', 'ordersByName', 'orders');
    // Direct DynamoDB Scan (ADR-0009) — a plain listing with no business logic, unlike
    // createCampaign/endCampaign which stay Lambda for their TransactWriteItems fan-out.
    this.resolver(campaignsDs, 'CampaignsResolver', 'Query', 'campaigns', 'pricing');

    // User profile — Cognito-only. Both myProfile (lazy provisioning via if_not_exists) and
    // updateProfile are direct DynamoDB UpdateItem resolvers (ADR-0009/ADR-0017).
    this.resolver(userProfilesDs, 'MyProfileResolver', 'Query', 'myProfile', 'user');
    this.resolver(userProfilesDs, 'UpdateProfileResolver', 'Mutation', 'updateProfile', 'user');

    // Basket mutations — storeBasket/deleteBasket allow Cognito OR API_KEY (guest via BFF);
    // checkoutBasket/mergeBasket are Cognito-only (see ADR-0016). storeBasket is a direct
    // DynamoDB PutItem resolver (ADR-0009).
    this.resolver(cartsDs, 'StoreBasketResolver', 'Mutation', 'storeBasket', 'basket');
    this.resolver(checkoutDs, 'CheckoutBasketResolver', 'Mutation', 'checkoutBasket', 'basket');
    this.resolver(mergeBasketDs, 'MergeBasketResolver', 'Mutation', 'mergeBasket', 'basket');
    this.resolver(cartsDs, 'DeleteBasketResolver', 'Mutation', 'deleteBasket', 'basket');
    this.pipelineResolver(reviewsDs, 'CreateReviewResolver', 'Mutation', 'createReview', 'reviews', [
      'Mutation.createReview.checkExisting.js',
      'Mutation.createReview.upsert.js',
    ]);

    // Admin/Seller mutations (group check in resolver)
    // Lambda resolver — batch presigned POSTs for direct browser->S3 uploads (ADR-0034).
    this.resolver(
      presignImageUploadDs, 'CreateProductImageUploadResolver', 'Mutation', 'createProductImageUpload', 'products',
    );
    this.resolver(productsDs, 'CreateProductResolver', 'Mutation', 'createProduct', 'products');
    // HTTP resolver → Step Functions Express saga (ADR-0032): product + nominal price in one
    // synchronous execution, with a compensating product delete if the price write fails.
    this.resolver(
      sfnDs, 'CreateProductWithPriceResolver', 'Mutation', 'createProductWithPrice', 'products',
      { __STATE_MACHINE_ARN__: this.productCreateSaga.stateMachineArn },
    );
    this.resolver(productsDs, 'UpdateProductResolver', 'Mutation', 'updateProduct', 'products');
    this.resolver(productsDs, 'DeleteProductResolver', 'Mutation', 'deleteProduct', 'products');

    // Admin-only mutations (group check in resolver)
    this.resolver(orderingDs, 'DeleteOrderResolver', 'Mutation', 'deleteOrder', 'orders');

    // Admin/Seller mutations — Pricing (ADR-0026). setNominalPrice is decoupled from
    // createProduct/updateProduct (see Mutation.createProduct.js); campaigns are Admin-only.
    // setNominalPrice is a direct DynamoDB UpdateItem resolver (ADR-0009).
    this.resolver(pricesDs, 'SetNominalPriceResolver', 'Mutation', 'setNominalPrice', 'pricing');
    this.resolver(createCampaignDs, 'CreateCampaignResolver', 'Mutation', 'createCampaign', 'pricing');
    this.resolver(endCampaignDs, 'EndCampaignResolver', 'Mutation', 'endCampaign', 'pricing');

    // Admin-only mutation — configures a payment-gateway provider's cost table (ADR-0028).
    // Direct DynamoDB UpdateItem resolver (ADR-0009).
    this.resolver(gatewayCostsDs, 'SetGatewayCostResolver', 'Mutation', 'setGatewayCost', 'pricing');

    // Challenges (ADR-0045) — Lambda resolvers; both escalate past the direct-resolver default
    // because grading/hinting need the stored answer key plus a conditional multi-item write.
    this.resolver(
      submitChallengeAnswerDs, 'SubmitChallengeAnswerResolver', 'Mutation', 'submitChallengeAnswer', 'challenges',
    );
    this.resolver(
      revealChallengeHintDs, 'RevealChallengeHintResolver', 'Mutation', 'revealChallengeHint', 'challenges',
    );
    this.resolver(
      redeemChallengePointsDs, 'RedeemChallengePointsResolver', 'Mutation', 'redeemChallengePoints', 'challenges',
    );
  }
}
