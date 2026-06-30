import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import { Construct } from 'constructs';
import { BasketDynamoDB } from '../constructs/basket-dynamodb';
import { BasketLambdas } from '../constructs/basket-lambdas';

export class BasketStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Use the default VPC so the DAX cluster and Lambda functions share the
    // same network without provisioning a dedicated VPC (dev account convenience).
    const vpc = ec2.Vpc.fromLookup(this, 'DefaultVpc', { isDefault: true });

    const dynamoDB = new BasketDynamoDB(this, 'BasketDynamoDB');

    const lambdas = new BasketLambdas(this, 'BasketLambdas', {
      shoppingCartsTable: dynamoDB.shoppingCartsTable,
      couponsTable: dynamoDB.couponsTable,
      vpc,
    });

    new cdk.CfnOutput(this, 'ShoppingCartsTableName', {
      value: dynamoDB.shoppingCartsTable.tableName,
      exportName: `${this.stackName}-ShoppingCartsTable`,
    });
    new cdk.CfnOutput(this, 'CouponsTableName', {
      value: dynamoDB.couponsTable.tableName,
      exportName: `${this.stackName}-CouponsTable`,
    });
    new cdk.CfnOutput(this, 'StreamPublisherArn', {
      value: lambdas.streamPublisher.functionArn,
      exportName: `${this.stackName}-StreamPublisherArn`,
    });
    new cdk.CfnOutput(this, 'StoreBasketUrl', {
      value: lambdas.storeBasketUrl.url,
      exportName: `${this.stackName}-StoreBasketUrl`,
    });
    new cdk.CfnOutput(this, 'CheckoutBasketUrl', {
      value: lambdas.checkoutBasketUrl.url,
      exportName: `${this.stackName}-CheckoutBasketUrl`,
    });
  }
}
