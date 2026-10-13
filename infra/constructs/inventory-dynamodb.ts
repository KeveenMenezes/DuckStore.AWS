import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class InventoryDynamoDB extends Construct {
  public readonly warehousesTable: dynamodb.Table;
  public readonly stockTable: dynamodb.Table;
  public readonly movementsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // PK=WarehouseId (ADR-0052 §2). Two item types: the warehouse (<uuid>, Type=Warehouse) and its
    // code guard (CODE#<code>, Type=CodeGuard), written together by createWarehouse so a code can
    // never be taken twice. No GSI — warehouses are tens of items, listed by a Type-filtered Scan.
    // No stream: nothing reacts to a warehouse change.
    this.warehousesTable = new dynamodb.Table(this, 'WarehousesTable', {
      tableName: 'inventory-warehouses',
      partitionKey: { name: 'WarehouseId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // PK=ProductId, SK=WarehouseId: one balance per product × warehouse (ADR-0052 §7). GSI1 lists a
    // warehouse's balances for its stock view. Written only by Inventory.Function, in the same
    // transaction as the ledger row.
    this.stockTable = new dynamodb.Table(this, 'StockTable', {
      tableName: 'inventory-stock',
      partitionKey: { name: 'ProductId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'WarehouseId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
    this.stockTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'WarehouseId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'ProductId', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    // The immutable movements ledger (ADR-0052 §7): PK=ProductId, SK=MovementId (the client's
    // idempotency key, so a replay can't write twice). LSI1 is a product's history by date and must
    // exist from creation; GSI1 is a warehouse's history by date.
    this.movementsTable = new dynamodb.Table(this, 'MovementsTable', {
      tableName: 'inventory-movements',
      partitionKey: { name: 'ProductId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'MovementId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
    this.movementsTable.addLocalSecondaryIndex({
      indexName: 'LSI1',
      sortKey: { name: 'CreatedAt', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });
    this.movementsTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'WarehouseId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'CreatedAt', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });
  }
}
