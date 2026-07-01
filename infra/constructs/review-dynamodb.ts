import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class ReviewDynamoDB extends Construct {
  public readonly reviewsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // PK=Id (UUID). GSI1 backs the AppSync direct resolver that lists a product's
    // reviews newest-first (GSI1PK=ProductId, GSI1SK=CreatedAt, ProjectionType.ALL).
    // Stream feeds review-reviews-event-publisher via CDC (ADR-0005/ADR-0008).
    this.reviewsTable = new dynamodb.Table(this, 'ReviewsTable', {
      tableName: 'reviews',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_IMAGE,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.reviewsTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'GSI1PK', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'GSI1SK', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });
  }
}
