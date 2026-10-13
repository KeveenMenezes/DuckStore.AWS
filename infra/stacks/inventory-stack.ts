import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { InventoryDynamoDB } from '../constructs/inventory-dynamodb';
import { InventoryLambdas } from '../constructs/inventory-lambdas';

// Inventory bounded context (ADR-0052). The warehouse fields are direct AppSync resolvers; the stock
// movements are the Lambdas below (§7). AppSyncStack reaches the tables and the functions by their
// fixed names.
export class InventoryStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new InventoryDynamoDB(this, 'InventoryDynamoDB');

    new InventoryLambdas(this, 'InventoryLambdas', {
      warehousesTable: dynamoDB.warehousesTable,
      stockTable: dynamoDB.stockTable,
      movementsTable: dynamoDB.movementsTable,
    });

    new cdk.CfnOutput(this, 'WarehousesTableName', {
      value: dynamoDB.warehousesTable.tableName,
    });
  }
}
