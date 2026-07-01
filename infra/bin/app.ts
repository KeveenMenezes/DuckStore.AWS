#!/usr/bin/env node
import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import { CatalogStack } from '../stacks/catalog-stack';
import { BasketStack } from '../stacks/basket-stack';
import { OrderingStack } from '../stacks/ordering-stack';
import { ReviewStack } from '../stacks/review-stack';
import { AppSyncStack } from '../stacks/appsync-stack';
import { SpaStack } from '../stacks/spa-stack';

const app = new cdk.App();

const env = {
  account: process.env.CDK_DEFAULT_ACCOUNT,
  region: process.env.CDK_DEFAULT_REGION,
};

new CatalogStack(app, 'DuckStoreCatalogStack', {
  env,
  description:
    'DuckStore Catalog service — DynamoDB tables, Lambda functions, and EventBridge wiring',
});

new BasketStack(app, 'DuckStoreBasketStack', {
  env,
  description:
    'DuckStore Basket service — DynamoDB tables (shopping-carts, coupons) and Lambda functions',
});

new OrderingStack(app, 'DuckStoreOrderingStack', {
  env,
  description:
    'DuckStore Ordering service — DynamoDB tables (ordering + GSI1, ordering-processed-events) and Lambda functions',
});

new ReviewStack(app, 'DuckStoreReviewStack', {
  env,
  description:
    'DuckStore Review service — DynamoDB table (reviews + GSI1) and CDC Lambda (ReviewCreatedEvent → EventBridge)',
});

new AppSyncStack(app, 'DuckStoreAppSyncStack', {
  env,
  spaBaseUrl: app.node.tryGetContext('spaBaseUrl') ?? 'http://localhost:3000',
  description:
    'DuckStore AppSync API — Cognito UserPool (RBAC groups), DynamoDB direct resolvers, Lambda resolvers',
});

// SpaStack's constructs read .open-next/* build output straight off disk at
// synthesis time (BucketDeployment/Lambda asset paths). Every `cdk` command
// constructs the whole App regardless of which stack is targeted, so without
// this guard, any OTHER stack's deploy (which never runs `open-next build`)
// would fail here too. Only deploy-spa-cdk.yml builds the SPA first.
const openNextDir = path.join(
  __dirname,
  '..',
  '..',
  'src/WebApps/Shopping.Web.SPA.React/.open-next',
);
if (fs.existsSync(openNextDir)) {
  new SpaStack(app, 'DuckStoreSpaStack', {
    env,
    environmentName: app.node.tryGetContext('environmentName') ?? 'dev',
    hostedZoneDomainName: app.node.tryGetContext('hostedZoneDomainName') ?? 'keveenmenezes.com',
    // Cross-stack references into DuckStoreAppSyncStack's CfnOutputs — avoids
    // duplicating these values as separate GitHub secrets/vars.
    appsyncUrl: cdk.Fn.importValue('DuckStoreAppSyncStack-ApiUrl'),
    appsyncApiKey: cdk.Fn.importValue('DuckStoreAppSyncStack-ApiKey'),
    cognitoClientId: cdk.Fn.importValue('DuckStoreAppSyncStack-UserPoolClientId'),
    cognitoHostedUiUrl: cdk.Fn.importValue('DuckStoreAppSyncStack-HostedUiUrl'),
    description:
      'DuckStore SPA — OpenNext (Next.js on Lambda) behind CloudFront with a custom domain',
  });
} else {
  console.warn(
    `Skipping DuckStoreSpaStack: ${openNextDir} not found. Run "pnpm build:opennext" in the SPA first.`,
  );
}
