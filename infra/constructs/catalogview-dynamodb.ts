import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

// CatalogView's only datastore (ADR-0030, supersedes ADR-0027's OpenSearch design). No stream —
// nothing downstream of CatalogView reads this table via CDC; it's the read-side leaf.
export class CatalogViewDynamoDB extends Construct {
  public readonly catalogViewProductsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    this.catalogViewProductsTable = new dynamodb.Table(this, 'CatalogViewProductsTable', {
      tableName: 'catalogview-products',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
