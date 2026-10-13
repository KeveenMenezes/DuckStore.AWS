import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { Construct } from 'constructs';
import {
  DOTNET_ARCH,
  DOTNET_MEMORY_MB,
  DOTNET_RUNTIME,
  dotnetLambdaCode,
} from './dotnet-lambda-code';

export interface InventoryLambdasProps {
  readonly warehousesTable: dynamodb.Table;
  readonly stockTable: dynamodb.Table;
  readonly movementsTable: dynamodb.Table;
}

// The three stock movements (ADR-0052 §7). Each is invoked only by its AppSync mutation — no Function
// URL, no event source, so no DLQ: a failure is returned to the caller. Each reads the warehouse and
// the balance(s), then writes balance + ledger row + warehouse check in one TransactWriteItems.
export class InventoryLambdas extends Construct {
  public readonly recordStockReceipt: lambda.Function;
  public readonly recordStockAdjustment: lambda.Function;
  public readonly transferStock: lambda.Function;

  constructor(scope: Construct, id: string, props: InventoryLambdasProps) {
    super(scope, id);

    const { warehousesTable, stockTable, movementsTable } = props;

    // One ZIP for the service; each Lambda picks its handler through ANNOTATIONS_HANDLER (ADR-0042).
    const inventoryCode = dotnetLambdaCode(
      'src/Services/Inventory',
      'src/Services/Inventory/Inventory.Function/Inventory.Function.csproj',
    );

    const movementFunction = (constructId: string, functionName: string, handler: string, description: string) => {
      const fn = new lambda.Function(this, constructId, {
        functionName,
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        runtime: DOTNET_RUNTIME,
        // provided.al2023 runs the file named `bootstrap`; this value is inert.
        handler: 'bootstrap',
        code: inventoryCode,
        timeout: cdk.Duration.seconds(30),
        memorySize: DOTNET_MEMORY_MB,
        description,
        environment: { ANNOTATIONS_HANDLER: handler },
      });
      // Consistent reads of the warehouse (ConditionCheckItem included), the balance and the ledger
      // rows (replay), then exactly the writes the transaction performs: UpdateItem on the balance,
      // PutItem of the ledger row. No DeleteItem anywhere, so IAM itself keeps the ledger immutable.
      warehousesTable.grantReadData(fn);
      stockTable.grantReadData(fn);
      stockTable.grant(fn, 'dynamodb:UpdateItem');
      movementsTable.grantReadData(fn);
      movementsTable.grant(fn, 'dynamodb:PutItem');
      return fn;
    };

    // 1. inventory-record-stock-receipt  (AppSync Invoke — Mutation.recordStockReceipt)
    this.recordStockReceipt = movementFunction(
      'RecordStockReceipt', 'inventory-record-stock-receipt', 'RecordStockReceipt',
      'Records a stock receipt (with invoice) into an active warehouse and its ledger row');

    // 2. inventory-record-stock-adjustment  (AppSync Invoke — Mutation.recordStockAdjustment)
    this.recordStockAdjustment = movementFunction(
      'RecordStockAdjustment', 'inventory-record-stock-adjustment', 'RecordStockAdjustment',
      'Records a stock adjustment (increase or decrease, with a reason) and its ledger row');

    // 3. inventory-transfer-stock  (AppSync Invoke — Mutation.transferStock)
    this.transferStock = movementFunction(
      'TransferStock', 'inventory-transfer-stock', 'TransferStock',
      'Moves stock between two warehouses: both balances and both ledger rows in one transaction');
  }
}
