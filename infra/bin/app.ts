#!/usr/bin/env node
import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import { CatalogStack } from '../stacks/catalog-stack';
import { BasketStack } from '../stacks/basket-stack';
import { OrderingStack } from '../stacks/ordering-stack';
import { ReviewStack } from '../stacks/review-stack';
import { UserStack } from '../stacks/user-stack';
import { AppSyncStack } from '../stacks/appsync-stack';
import { SpaStack } from '../stacks/spa-stack';

const app = new cdk.App();

const env = {
  account: process.env.CDK_DEFAULT_ACCOUNT,
  region: process.env.CDK_DEFAULT_REGION,
};

// Shared by AppSyncStack (Cognito callback/logout allowlist) and SpaStack
// (CloudFront custom domain). Must be derived identically in both so the
// deployed SPA's redirect_uri matches a registered Cognito callback URL.
const environmentName = app.node.tryGetContext('environmentName') ?? 'dev';
const hostedZoneDomainName =
  app.node.tryGetContext('hostedZoneDomainName') ?? 'keveenmenezes.com';
const spaDomainUrl = `https://${environmentName}-duckstore.${hostedZoneDomainName}`;

// Project-wide tags — cascade to every resource in every stack (buckets, tables,
// Lambdas, ...). This is how DuckStore resources are identified across the AWS
// account (S3 console filter, Cost Explorer cost allocation, IAM conditions),
// independent of the auto-generated physical names.
cdk.Tags.of(app).add('Project', 'DuckStore');
cdk.Tags.of(app).add('ManagedBy', 'CDK');
cdk.Tags.of(app).add('Environment', environmentName);

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

new UserStack(app, 'DuckStoreUserStack', {
  env,
  description:
    'DuckStore User service — DynamoDB table (user-profiles) and the lazy-provisioning GetProfile Lambda',
});

new AppSyncStack(app, 'DuckStoreAppSyncStack', {
  env,
  // Allow both the local dev server and the deployed CloudFront domain so the
  // same app client works in dev (pnpm dev) and prod without a redirect_mismatch.
  spaBaseUrls: ['http://localhost:3000', spaDomainUrl],
  // Social federation. Client IDs are public → committed in cdk.json context.
  // Client secrets live in Secrets Manager (bootstrapped out-of-band) and are
  // resolved by CloudFormation at deploy via a dynamic reference — never in the
  // repo, and deploys stay self-sufficient (no -c needed). Absent context id =
  // provider's button is simply not rendered.
  googleClientId: app.node.tryGetContext('googleClientId'),
  googleClientSecret: cdk.SecretValue.secretsManager('duckstore/federation/google'),
  amazonClientId: app.node.tryGetContext('amazonClientId'),
  amazonClientSecret: cdk.SecretValue.secretsManager('duckstore/federation/amazon'),
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
    environmentName,
    hostedZoneDomainName,
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
