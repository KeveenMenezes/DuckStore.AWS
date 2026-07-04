#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { CatalogStack } from '../stacks/catalog-stack';
import { BasketStack } from '../stacks/basket-stack';
import { OrderingStack } from '../stacks/ordering-stack';
import { ReviewStack } from '../stacks/review-stack';
import { UserStack } from '../stacks/user-stack';
import { AppSyncStack } from '../stacks/appsync-stack';

const app = new cdk.App();

const env = {
  account: process.env.CDK_DEFAULT_ACCOUNT,
  region: process.env.CDK_DEFAULT_REGION,
};

// Used by AppSyncStack (Cognito callback/logout allowlist). The SPA itself no
// longer deploys via CDK (see sst.config.ts in Shopping.Web.SPA.React) — this
// must still compute the exact same domain that app derives, so the deployed
// SPA's redirect_uri matches a registered Cognito callback URL.
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

// The SPA (OpenNext/Next.js on Lambda + CloudFront) deploys via SST, not CDK
// — see src/WebApps/Shopping.Web.SPA.React/sst.config.ts (`sst deploy`). It
// reads DuckStoreAppSyncStack's CloudFormation exports directly
// (`aws.cloudformation.getExportOutput`) instead of `cdk.Fn.importValue`.
