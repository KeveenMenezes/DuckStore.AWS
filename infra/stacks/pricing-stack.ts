import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { PricingDynamoDB } from '../constructs/pricing-dynamodb';
import { PricingLambdas } from '../constructs/pricing-lambdas';

export class PricingStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new PricingDynamoDB(this, 'PricingDynamoDB');

    const lambdas = new PricingLambdas(this, 'PricingLambdas', {
      pricesTable: dynamoDB.pricesTable,
      campaignsTable: dynamoDB.campaignsTable,
      productDiscountsTable: dynamoDB.productDiscountsTable,
      gatewayCostsTable: dynamoDB.gatewayCostsTable,
      processedEventsTable: dynamoDB.processedEventsTable,
      customerDiscountsTable: dynamoDB.customerDiscountsTable,
    });

    new cdk.CfnOutput(this, 'PricesTableName', {
      value: dynamoDB.pricesTable.tableName,
      exportName: `${this.stackName}-PricesTable`,
    });
    new cdk.CfnOutput(this, 'CampaignsTableName', {
      value: dynamoDB.campaignsTable.tableName,
      exportName: `${this.stackName}-CampaignsTable`,
    });
    new cdk.CfnOutput(this, 'ProductDiscountsTableName', {
      value: dynamoDB.productDiscountsTable.tableName,
      exportName: `${this.stackName}-ProductDiscountsTable`,
    });
    new cdk.CfnOutput(this, 'ProductDeletedConsumerArn', {
      value: lambdas.productDeletedConsumer.functionArn,
      exportName: `${this.stackName}-ProductDeletedConsumerArn`,
    });
    new cdk.CfnOutput(this, 'GatewayCostsTableName', {
      value: dynamoDB.gatewayCostsTable.tableName,
      exportName: `${this.stackName}-GatewayCostsTable`,
    });
    new cdk.CfnOutput(this, 'CustomerDiscountsTableName', {
      value: dynamoDB.customerDiscountsTable.tableName,
      exportName: `${this.stackName}-CustomerDiscountsTable`,
    });
    new cdk.CfnOutput(this, 'PointsRedeemedConsumerArn', {
      value: lambdas.pointsRedeemedConsumer.functionArn,
      exportName: `${this.stackName}-PointsRedeemedConsumerArn`,
    });
    new cdk.CfnOutput(this, 'PaymentAuthorizedConsumerArn', {
      value: lambdas.paymentAuthorizedConsumer.functionArn,
      exportName: `${this.stackName}-PaymentAuthorizedConsumerArn`,
    });
  }
}
