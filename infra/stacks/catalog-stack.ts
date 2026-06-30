import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { CatalogDynamoDB } from '../constructs/catalog-dynamodb';
import { CatalogLambdas } from '../constructs/catalog-lambdas';

export class CatalogStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const spaWebhookUrl = new cdk.CfnParameter(this, 'SpaWebhookUrl', {
      type: 'String',
      description:
        'Base URL of the Next.js SPA (e.g. https://app.example.com). ' +
        'The catalog-updated consumer appends /api/webhooks/catalog-updated.',
    });

    const webhookSecret = new cdk.CfnParameter(this, 'CatalogWebhookSecret', {
      type: 'String',
      noEcho: true,
      description:
        'Secret sent in x-webhook-secret to authenticate ISR webhook calls to the SPA.',
    });

    const dynamoDB = new CatalogDynamoDB(this, 'CatalogDynamoDB');

    const lambdas = new CatalogLambdas(this, 'CatalogLambdas', {
      productsTable: dynamoDB.productsTable,
      categoriesTable: dynamoDB.categoriesTable,
      processedEventsTable: dynamoDB.processedEventsTable,
      spaWebhookUrl: spaWebhookUrl.valueAsString,
      catalogWebhookSecret: webhookSecret.valueAsString,
    });

    new cdk.CfnOutput(this, 'ProductsTableName', {
      value: dynamoDB.productsTable.tableName,
      exportName: `${this.stackName}-ProductsTable`,
    });
    new cdk.CfnOutput(this, 'CategoriesTableName', {
      value: dynamoDB.categoriesTable.tableName,
      exportName: `${this.stackName}-CategoriesTable`,
    });
    new cdk.CfnOutput(this, 'ProcessedEventsTableName', {
      value: dynamoDB.processedEventsTable.tableName,
      exportName: `${this.stackName}-ProcessedEventsTable`,
    });
    new cdk.CfnOutput(this, 'EventBusName', {
      value: lambdas.eventBus.eventBusName,
      exportName: `${this.stackName}-EventBusName`,
    });
    new cdk.CfnOutput(this, 'StreamPublisherArn', {
      value: lambdas.streamPublisher.functionArn,
      exportName: `${this.stackName}-StreamPublisherArn`,
    });
    new cdk.CfnOutput(this, 'CatalogUpdatedConsumerArn', {
      value: lambdas.catalogUpdatedConsumer.functionArn,
      exportName: `${this.stackName}-CatalogUpdatedConsumerArn`,
    });
    new cdk.CfnOutput(this, 'ReviewCreatedConsumerArn', {
      value: lambdas.reviewCreatedConsumer.functionArn,
      exportName: `${this.stackName}-ReviewCreatedConsumerArn`,
    });
  }
}
