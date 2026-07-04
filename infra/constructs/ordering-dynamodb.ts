import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class OrderingDynamoDB extends Construct {
  public readonly orderingTable: dynamodb.Table;
  public readonly processedEventsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Single-item-per-order design: PK=Id, OrderItems embedded as a list attribute.
    // GSI1 (GSI1PK=CUSTOMER#{id}, GSI1SK=CreatedAt) lists orders by customer with
    // ProjectionType.ALL, avoiding a follow-up GetItem per result.
    // Stream feeds ordering-order-created-publisher via CDC (ADR-0005).
    this.orderingTable = new dynamodb.Table(this, 'OrderingTable', {
      tableName: 'ordering',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      // NEW_AND_OLD_IMAGES so stream-publisher rules can detect transitions (ADR-0019).
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.orderingTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'GSI1PK', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'GSI1SK', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    // Idempotency inbox for ordering-basket-checkout-consumer (ADR-0011).
    // PK=eventId written inside the same TransactWriteItems as the order (atomic).
    this.processedEventsTable = new dynamodb.Table(this, 'ProcessedEventsTable', {
      tableName: 'ordering-processed-events',
      partitionKey: { name: 'PK', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
