import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class CatalogDynamoDB extends Construct {
  public readonly productsTable: dynamodb.Table;
  public readonly categoriesTable: dynamodb.Table;
  public readonly processedEventsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Stream feeds catalog-stream-event-publisher via CDC (ADR-0005).
    this.productsTable = new dynamodb.Table(this, 'ProductsTable', {
      tableName: 'products',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.categoriesTable = new dynamodb.Table(this, 'CategoriesTable', {
      tableName: 'categories',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // Idempotency inbox for catalog-review-created-consumer (ADR-0011).
    // PK attribute is "PK" — matches ProcessedIntegrationEvent property and
    // DynamoIdempotentEventConsumer which writes ["PK"] = eventId.
    this.processedEventsTable = new dynamodb.Table(this, 'ProcessedEventsTable', {
      tableName: 'catalog-processed-events',
      partitionKey: { name: 'PK', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
