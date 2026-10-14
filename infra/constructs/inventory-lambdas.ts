import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';
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

// The three stock movements (ADR-0052 §7) are invoked only by their AppSync mutations — no Function
// URL, no event source, so no DLQ: a failure is returned to the caller. The two storefront-total
// publishers (§8) are CDC: DynamoDB Streams in, EventBridge out, with the context DLQ.
export class InventoryLambdas extends Construct {
  public readonly recordStockReceipt: lambda.Function;
  public readonly recordStockAdjustment: lambda.Function;
  public readonly transferStock: lambda.Function;
  public readonly stockStreamPublisher: lambda.Function;
  public readonly warehousesStreamPublisher: lambda.Function;

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

    // EventBridge bus — created by FoundationStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');
    // Shared dead-letter queue for Inventory's async processing; a non-empty queue trips the
    // inventory-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'inventory' });

    const streamPublisher = (
      constructId: string, functionName: string, handler: string, description: string, table: dynamodb.Table,
    ) => {
      const fn = new lambda.Function(this, constructId, {
        functionName,
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        runtime: DOTNET_RUNTIME,
        handler: 'bootstrap',
        code: inventoryCode,
        // A warehouse deactivation recomputes and publishes every product it holds.
        timeout: cdk.Duration.seconds(60),
        memorySize: DOTNET_MEMORY_MB,
        description,
        environment: { ANNOTATIONS_HANDLER: handler, EventBridge__BusName: eventBus.eventBusName },
      });
      fn.addEventSource(new lambdaEventSources.DynamoEventSource(table, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (shard/sequence metadata, not the
        // payload — redrive by re-reading the stream).
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }));
      // Reads only: the total is recomputed from the balances and their warehouses.
      stockTable.grantReadData(fn);
      warehousesTable.grantReadData(fn);
      eventBus.grantPutEventsTo(fn);
      return fn;
    };

    // 4. inventory-stock-stream-publisher  (inventory-stock stream → ProductStockChangedEvent)
    this.stockStreamPublisher = streamPublisher(
      'StockStreamPublisher', 'inventory-stock-stream-publisher', 'InventoryStockStreamPublisher',
      'CDC: republishes a product\'s storefront total (active warehouses) on every balance change',
      stockTable);

    // 5. inventory-warehouses-stream-publisher  (inventory-warehouses stream → one event per product)
    this.warehousesStreamPublisher = streamPublisher(
      'WarehousesStreamPublisher', 'inventory-warehouses-stream-publisher', 'InventoryWarehousesStreamPublisher',
      'CDC: republishes the storefront total of every product in a warehouse whose Active flag changed',
      warehousesTable);
  }
}
