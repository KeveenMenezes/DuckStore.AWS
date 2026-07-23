import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { PaymentDynamoDB } from '../constructs/payment-dynamodb';
import { PaymentLambdas, PaymentGatewayLambdas } from '../constructs/payment-lambdas';

export class PaymentStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new PaymentDynamoDB(this, 'PaymentDynamoDB');

    const lambdas = new PaymentLambdas(this, 'PaymentLambdas', {
      paymentsTable: dynamoDB.paymentsTable,
      processedEventsTable: dynamoDB.processedEventsTable,
    });

    // PaymentGateway.Function is a separate deployable unit (ADR-0025) — own DLQ, own image,
    // no DynamoDB — instantiated here rather than in its own stack for the same reason it's
    // registered alongside Payment in AppHost: a single Lambda doesn't warrant a stack of its own.
    const gatewayLambdas = new PaymentGatewayLambdas(this, 'PaymentGatewayLambdas');

    new cdk.CfnOutput(this, 'PaymentsTableName', {
      value: dynamoDB.paymentsTable.tableName,
      exportName: `${this.stackName}-PaymentsTable`,
    });
    new cdk.CfnOutput(this, 'ProcessedEventsTableName', {
      value: dynamoDB.processedEventsTable.tableName,
      exportName: `${this.stackName}-ProcessedEventsTable`,
    });
    new cdk.CfnOutput(this, 'BasketCheckoutConsumerArn', {
      value: lambdas.basketCheckoutConsumer.functionArn,
      exportName: `${this.stackName}-BasketCheckoutConsumerArn`,
    });
    new cdk.CfnOutput(this, 'PaymentRequestedPublisherArn', {
      value: lambdas.paymentRequestedPublisher.functionArn,
      exportName: `${this.stackName}-PaymentRequestedPublisherArn`,
    });
    new cdk.CfnOutput(this, 'PaymentAuthorizedConsumerArn', {
      value: lambdas.paymentAuthorizedConsumer.functionArn,
      exportName: `${this.stackName}-PaymentAuthorizedConsumerArn`,
    });
    new cdk.CfnOutput(this, 'PaymentDeclinedConsumerArn', {
      value: lambdas.paymentDeclinedConsumer.functionArn,
      exportName: `${this.stackName}-PaymentDeclinedConsumerArn`,
    });
    new cdk.CfnOutput(this, 'PaymentGatewayConsumerArn', {
      value: gatewayLambdas.paymentRequestedConsumer.functionArn,
      exportName: `${this.stackName}-PaymentGatewayConsumerArn`,
    });
  }
}
