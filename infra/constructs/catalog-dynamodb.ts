import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class CatalogDynamoDB extends Construct {
  public readonly productsTable: dynamodb.Table;
  public readonly categoriesTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Stream feeds catalog-products-stream-publisher via CDC (ADR-0005).
    this.productsTable = new dynamodb.Table(this, 'ProductsTable', {
      tableName: 'products',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // Stream feeds catalog-categories-stream-publisher via CDC — a category rename needs to
    // propagate its new name into CatalogView's product documents.
    this.categoriesTable = new dynamodb.Table(this, 'CategoriesTable', {
      tableName: 'categories',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
