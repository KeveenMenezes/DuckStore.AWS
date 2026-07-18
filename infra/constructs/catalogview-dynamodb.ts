import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

// CatalogView's only datastore (ADR-0030). Streamed (ADR-0035): CatalogView's own CDC publisher
// emits CatalogViewProductSyncedEvent/CatalogViewProductDeletedEvent off this table's writes, so
// the SPA revalidator invalidates CloudFront only after CatalogView's write has committed, instead
// of racing it via a direct subscription to upstream (Catalog/Pricing/Review) events.
export class CatalogViewDynamoDB extends Construct {
  public readonly catalogViewProductsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    this.catalogViewProductsTable = new dynamodb.Table(this, 'CatalogViewProductsTable', {
      tableName: 'catalogview-products',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      // NEW_AND_OLD_IMAGES (not just NEW_IMAGE): a REMOVE record only carries OldImage, and the
      // delete rule needs Old.Id to know which product was removed.
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
    });

    // GSI1PK is a constant ("PRODUCT") so every item lives in one partition, sorted by
    // AverageRating — lets the unfiltered/rating-only browse path (Query.products.js with no
    // free-text `query`) use a Query instead of a full-table Scan. Free-text `query` still
    // requires a Scan (DynamoDB has no substring search) — that limitation is accepted, same as
    // ADR-0030's tradeoff for this table.
    this.catalogViewProductsTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'GSI1PK', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'GSI1SK', type: dynamodb.AttributeType.NUMBER },
      projectionType: dynamodb.ProjectionType.ALL,
    });
  }
}
