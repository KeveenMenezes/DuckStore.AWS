import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { InventoryDynamoDB } from '../constructs/inventory-dynamodb';

// Inventory bounded context (ADR-0052). It has no Lambda: the warehouse fields are direct
// AppSync resolvers, and AppSyncStack reaches the table by its fixed name.
export class InventoryStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new InventoryDynamoDB(this, 'InventoryDynamoDB');

    new cdk.CfnOutput(this, 'WarehousesTableName', {
      value: dynamoDB.warehousesTable.tableName,
    });
  }
}
