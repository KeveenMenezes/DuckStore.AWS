import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class InventoryDynamoDB extends Construct {
  public readonly warehousesTable: dynamodb.Table;

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
  }
}
