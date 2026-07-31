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
    new cdk.CfnOutput(this, 'CatalogSyncConsumerArn', {
      value: lambdas.catalogSyncConsumer.functionArn,
      exportName: `${this.stackName}-CatalogSyncConsumerArn`,
    });
    new cdk.CfnOutput(this, 'ReviewSyncConsumerArn', {
      value: lambdas.reviewSyncConsumer.functionArn,
      exportName: `${this.stackName}-ReviewSyncConsumerArn`,
    });
    new cdk.CfnOutput(this, 'PricingSyncConsumerArn', {
      value: lambdas.pricingSyncConsumer.functionArn,
      exportName: `${this.stackName}-PricingSyncConsumerArn`,
    });
    new cdk.CfnOutput(this, 'ProductStreamPublisherArn', {
      value: lambdas.productStreamPublisher.functionArn,
      exportName: `${this.stackName}-ProductStreamPublisherArn`,
    });
  }
}
