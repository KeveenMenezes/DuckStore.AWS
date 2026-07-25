import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class PaymentDynamoDB extends Construct {
  public readonly paymentsTable: dynamodb.Table;
  public readonly processedEventsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Single-item-per-payment design, mirroring DynamoOrderRepository (ADR-0025).
    // GSI1 (GSI1PK=ORDER#{orderId}, GSI1SK=CreatedAt) lists payments by order with
    // ProjectionType.ALL, avoiding a follow-up GetItem per result.
    // Stream feeds payment-payments-stream-publisher via CDC (ADR-0005/0019).
    this.paymentsTable = new dynamodb.Table(this, 'PaymentsTable', {
      tableName: 'payments',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.paymentsTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'GSI1PK', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'GSI1SK', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    // Idempotency inbox for payment-basket-checkout-consumer and the two
    // payment-result-*-consumer Lambdas. PK=eventId written inside the same
    // TransactWriteItems as the payment row (atomic).
    this.processedEventsTable = new dynamodb.Table(this, 'ProcessedEventsTable', {
      tableName: 'payment-processed-events',
      partitionKey: { name: 'PK', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
