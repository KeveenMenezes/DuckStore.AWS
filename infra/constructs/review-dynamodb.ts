import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class ReviewDynamoDB extends Construct {
  public readonly reviewsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // PK=Id, a deterministic composite `${productId}#${base64url(userName)}` (ADR-0029) so a
    // PutItem is naturally an upsert — one active review per customer per product. GSI1 backs the
    // AppSync direct resolver that lists a product's reviews newest-first (GSI1PK=ProductId,
    // GSI1SK=CreatedAt, ProjectionType.ALL) and stays pinned to the original CreatedAt on edits.
    // NEW_AND_OLD_IMAGES (widened from NEW_IMAGE) lets the stream publisher diff old/new rating on
    // a MODIFY so CatalogView can apply a rating delta (ADR-0029), not just INSERT (ADR-0011).
    this.reviewsTable = new dynamodb.Table(this, 'ReviewsTable', {
      tableName: 'reviews',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
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
