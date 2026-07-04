import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { OrderingDynamoDB } from '../constructs/ordering-dynamodb';
import { OrderingLambdas } from '../constructs/ordering-lambdas';

export class OrderingStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new OrderingDynamoDB(this, 'OrderingDynamoDB');

    const lambdas = new OrderingLambdas(this, 'OrderingLambdas', {
      orderingTable: dynamoDB.orderingTable,
      processedEventsTable: dynamoDB.processedEventsTable,
    });

    new cdk.CfnOutput(this, 'OrderingTableName', {
      value: dynamoDB.orderingTable.tableName,
      exportName: `${this.stackName}-OrderingTable`,
    });
    new cdk.CfnOutput(this, 'ProcessedEventsTableName', {
      value: dynamoDB.processedEventsTable.tableName,
      exportName: `${this.stackName}-ProcessedEventsTable`,
    });
    new cdk.CfnOutput(this, 'BasketCheckoutConsumerArn', {
      value: lambdas.basketCheckoutConsumer.functionArn,
      exportName: `${this.stackName}-BasketCheckoutConsumerArn`,
    });
    new cdk.CfnOutput(this, 'OrderCreatedPublisherArn', {
      value: lambdas.orderCreatedPublisher.functionArn,
      exportName: `${this.stackName}-OrderCreatedPublisherArn`,
    });
  }
}
