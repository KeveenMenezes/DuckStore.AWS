#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { CatalogStack } from '../stacks/catalog-stack';
import { BasketStack } from '../stacks/basket-stack';
import { OrderingStack } from '../stacks/ordering-stack';
import { ReviewStack } from '../stacks/review-stack';
import { AppSyncStack } from '../stacks/appsync-stack';

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
