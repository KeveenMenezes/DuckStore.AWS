#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { CatalogStack } from '../stacks/catalog-stack';
import { BasketStack } from '../stacks/basket-stack';
import { OrderingStack } from '../stacks/ordering-stack';
import { PricingStack } from '../stacks/pricing-stack';
import { ReviewStack } from '../stacks/review-stack';
import { CatalogViewStack } from '../stacks/catalogview-stack';
import { UserStack } from '../stacks/user-stack';
import { AppSyncStack } from '../stacks/appsync-stack';
import { ManagementStack } from '../stacks/management-stack';
import { ProductImagesStack } from '../stacks/product-images-stack';
import { MonitoringStack } from '../stacks/monitoring-stack';

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
// Blazor management app (Managment.Web.Blazor) — same deterministic-domain trick, so
// the Cognito management client's callback URL is known before anything deploys.
const managementDomainName = `${environmentName}-management-duckstore.${hostedZoneDomainName}`;
const managementDomainUrl = `https://${managementDomainName}`;

// Project-wide tags — cascade to every resource in every stack (buckets, tables,
// Lambdas, ...). This is how DuckStore resources are identified across the AWS
// account (S3 console filter, Cost Explorer cost allocation, IAM conditions),
// independent of the auto-generated physical names.
cdk.Tags.of(app).add('Project', 'DuckStore');
cdk.Tags.of(app).add('ManagedBy', 'CDK');
cdk.Tags.of(app).add('Environment', environmentName);

// Deploy first: every DLQ alarm imports the duckstore-alerts topic by name
// (see importAlertsTopic in infra/constructs/context-dlq.ts).
new MonitoringStack(app, 'DuckStoreMonitoringStack', {
  env,
  alertsEmail: app.node.tryGetContext('alertsEmail') ?? process.env.ALERTS_EMAIL,
  description:
    'DuckStore shared alerting — duckstore-alerts SNS topic targeted by every CloudWatch alarm',
});

new CatalogStack(app, 'DuckStoreCatalogStack', {
  env,
  description:
    'DuckStore Catalog service — DynamoDB tables, Lambda functions, and EventBridge wiring',
});

new BasketStack(app, 'DuckStoreBasketStack', {
  env,
  description:
    'DuckStore Basket service — DynamoDB table (shopping-carts) and Lambda functions',
});

new OrderingStack(app, 'DuckStoreOrderingStack', {
  env,
  description:
    'DuckStore Ordering service — DynamoDB tables (ordering + GSI1, ordering-processed-events) and Lambda functions',
});

new PricingStack(app, 'DuckStorePricingStack', {
  env,
  description:
    'DuckStore Pricing service — DynamoDB tables (prices, campaigns, product-discounts, pricing-processed-events) and Lambda functions (ADR-0026)',
});

new ReviewStack(app, 'DuckStoreReviewStack', {
  env,
  description:
    'DuckStore Review service — DynamoDB table (reviews + GSI1) and CDC Lambda (ReviewCreatedEvent → EventBridge)',
});

new CatalogViewStack(app, 'DuckStoreCatalogViewStack', {
  env,
  description:
    'DuckStore CatalogView service — DynamoDB table (catalogview-products) and CDC consumer Lambdas (ADR-0030, supersedes ADR-0027)',
});

new UserStack(app, 'DuckStoreUserStack', {
  env,
  description:
    'DuckStore User service — DynamoDB table (user-profiles) and the lazy-provisioning GetProfile Lambda',
});

// The processor/presign Lambdas are Docker-bundled NodejsFunctions (sharp has no esbuild-safe
// path) — that bundling executes on every `cdk synth`/`cdk deploy` regardless of which stack
// is targeted, and requires QEMU on x86 CI runners to build the arm64 bundling image. Gated
// behind a context flag, same pattern as ManagementStack, so every other stack's deploy workflow
// (which doesn't set up QEMU) doesn't eagerly trigger this Docker build and fail.
if (app.node.tryGetContext('deployProductImages') === 'true') {
  new ProductImagesStack(app, 'DuckStoreProductImagesStack', {
    env,
    // Same deterministic-domain trick as the SPA/admin: the CDN base URL is known
    // before anything deploys, so clients can carry it in checked-in config.
    imageDomainName: `${environmentName}-img-duckstore.${hostedZoneDomainName}`,
    hostedZoneDomainName,
    // Browser presigned-POST uploads come from the Blazor management app (dev server + deployed).
    uploadOrigins: ['https://localhost:7300', managementDomainUrl],
    description:
      'DuckStore product image pipeline (ADR-0034) — originals/processed buckets, SQS + sharp processor, presign Lambda, image CDN',
  });
}

new AppSyncStack(app, 'DuckStoreAppSyncStack', {
  env,
  // Allow both the local dev server and the deployed CloudFront domain so the
  // same app client works in dev (pnpm dev) and prod without a redirect_mismatch.
  spaBaseUrls: ['http://localhost:3000', spaDomainUrl],
  // Blazor dev server (fixed port in launchSettings.json) + the deployed static site.
  // Cognito allows https localhost callback URLs.
  managementBaseUrls: ['https://localhost:7300', managementDomainUrl],
  // Social federation. Client IDs are public → committed in cdk.json context.
  // Client secrets live in SSM Parameter Store as SecureStrings (standard tier
  // is free, vs $0.40/secret/month in Secrets Manager — no rotation needed for
  // these) bootstrapped out-of-band, and are resolved by CloudFormation at
  // deploy via a {{resolve:ssm-secure:...}} dynamic reference — never in the
  // repo, and deploys stay self-sufficient (no -c needed). Absent context id =
  // provider's button is simply not rendered.
  googleClientId: app.node.tryGetContext('googleClientId'),
  googleClientSecret: cdk.SecretValue.ssmSecure('/duckstore/federation/google'),
  amazonClientId: app.node.tryGetContext('amazonClientId'),
  amazonClientSecret: cdk.SecretValue.ssmSecure('/duckstore/federation/amazon'),
  description:
    'DuckStore AppSync API — Cognito UserPool (RBAC groups), DynamoDB direct resolvers, Lambda resolvers',
});

// The management app's stack embeds the Blazor `dotnet publish` output as an S3
// asset, which must exist at synth time — so it's only instantiated behind the
// `-c deployManagement=true` flag (passed by deploy-management-cdk.yml after
// publishing). Without the flag every other stack still synths on a machine with no
// .NET build.
if (app.node.tryGetContext('deployManagement') === 'true') {
  new ManagementStack(app, 'DuckStoreManagementStack', {
    env,
    managementDomainName,
    hostedZoneDomainName,
    description:
      'DuckStore management app (Managment.Web.Blazor) — S3 static site behind CloudFront with OAC',
  });
}

// The SPA (OpenNext/Next.js on Lambda + CloudFront) deploys via SST, not CDK
// — see src/WebApps/Shopping.Web.SPA.React/sst.config.ts (`sst deploy`). It
// reads DuckStoreAppSyncStack's CloudFormation exports directly
// (`aws.cloudformation.getExportOutput`) instead of `cdk.Fn.importValue`.
