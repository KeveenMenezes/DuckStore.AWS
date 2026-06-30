#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { CatalogStack } from '../stacks/catalog-stack';

const app = new cdk.App();

new CatalogStack(app, 'DuckStoreCatalogStack', {
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: process.env.CDK_DEFAULT_REGION,
  },
  description:
    'DuckStore Catalog service — DynamoDB tables, Lambda functions, and EventBridge wiring',
});
