import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { CatalogViewDynamoDB } from '../constructs/catalogview-dynamodb';
import { CatalogViewLambdas } from '../constructs/catalogview-lambdas';

export class CatalogViewStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new CatalogViewDynamoDB(this, 'CatalogViewDynamoDB');

    const lambdas = new CatalogViewLambdas(this, 'CatalogViewLambdas', {
      catalogViewProductsTable: dynamoDB.catalogViewProductsTable,
    });

    new cdk.CfnOutput(this, 'CatalogViewProductsTableName', {
      value: dynamoDB.catalogViewProductsTable.tableName,
      exportName: `${this.stackName}-CatalogViewProductsTable`,
    });
    new cdk.CfnOutput(this, 'ProductSyncConsumerArn', {
      value: lambdas.productSyncConsumer.functionArn,
      exportName: `${this.stackName}-ProductSyncConsumerArn`,
    });
    new cdk.CfnOutput(this, 'ReviewAggregateConsumerArn', {
      value: lambdas.reviewAggregateConsumer.functionArn,
      exportName: `${this.stackName}-ReviewAggregateConsumerArn`,
    });
    new cdk.CfnOutput(this, 'ReviewUpdateAggregateConsumerArn', {
      value: lambdas.reviewUpdateAggregateConsumer.functionArn,
      exportName: `${this.stackName}-ReviewUpdateAggregateConsumerArn`,
    });
    new cdk.CfnOutput(this, 'PriceSyncConsumerArn', {
      value: lambdas.priceSyncConsumer.functionArn,
      exportName: `${this.stackName}-PriceSyncConsumerArn`,
    });
    new cdk.CfnOutput(this, 'CategorySyncConsumerArn', {
      value: lambdas.categorySyncConsumer.functionArn,
      exportName: `${this.stackName}-CategorySyncConsumerArn`,
    });
    new cdk.CfnOutput(this, 'ProductStreamPublisherArn', {
      value: lambdas.productStreamPublisher.functionArn,
      exportName: `${this.stackName}-ProductStreamPublisherArn`,
    });
  }
}
