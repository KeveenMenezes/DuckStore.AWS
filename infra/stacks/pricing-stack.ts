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
    new cdk.CfnOutput(this, 'SetNominalPriceArn', {
      value: lambdas.setNominalPrice.functionArn,
      exportName: `${this.stackName}-SetNominalPriceArn`,
    });
    new cdk.CfnOutput(this, 'GetInstallmentPlanArn', {
      value: lambdas.getInstallmentPlan.functionArn,
      exportName: `${this.stackName}-GetInstallmentPlanArn`,
    });
    new cdk.CfnOutput(this, 'GetBasketInstallmentPlanArn', {
      value: lambdas.getBasketInstallmentPlan.functionArn,
      exportName: `${this.stackName}-GetBasketInstallmentPlanArn`,
    });
    new cdk.CfnOutput(this, 'CreateCampaignArn', {
      value: lambdas.createCampaign.functionArn,
      exportName: `${this.stackName}-CreateCampaignArn`,
    });
    new cdk.CfnOutput(this, 'EndCampaignArn', {
      value: lambdas.endCampaign.functionArn,
      exportName: `${this.stackName}-EndCampaignArn`,
    });
    new cdk.CfnOutput(this, 'CatalogProductRemovedConsumerArn', {
      value: lambdas.catalogProductRemovedConsumer.functionArn,
      exportName: `${this.stackName}-CatalogProductRemovedConsumerArn`,
    });
    new cdk.CfnOutput(this, 'GatewayCostsTableName', {
      value: dynamoDB.gatewayCostsTable.tableName,
      exportName: `${this.stackName}-GatewayCostsTable`,
    });
    new cdk.CfnOutput(this, 'SetGatewayCostArn', {
      value: lambdas.setGatewayCost.functionArn,
      exportName: `${this.stackName}-SetGatewayCostArn`,
    });
  }
}
