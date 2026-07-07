import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { BasketDynamoDB } from '../constructs/basket-dynamodb';
import { BasketLambdas } from '../constructs/basket-lambdas';

export class BasketStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new BasketDynamoDB(this, 'BasketDynamoDB');

    const lambdas = new BasketLambdas(this, 'BasketLambdas', {
      shoppingCartsTable: dynamoDB.shoppingCartsTable,
    });

    new cdk.CfnOutput(this, 'ShoppingCartsTableName', {
      value: dynamoDB.shoppingCartsTable.tableName,
      exportName: `${this.stackName}-ShoppingCartsTable`,
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
