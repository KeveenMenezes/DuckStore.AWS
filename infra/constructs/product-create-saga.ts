import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as sfn from 'aws-cdk-lib/aws-stepfunctions';
import * as tasks from 'aws-cdk-lib/aws-stepfunctions-tasks';
import { Construct } from 'constructs';

// Step Functions Express saga behind the createProductWithPrice mutation (ADR-0032):
// writes the Catalog product and its Pricing nominal price in one synchronous execution
// (AppSync HTTP datasource → StartSyncExecution). The two tables belong to different
// bounded contexts (ADR-0026), so a compensating delete — not TransactWriteItems —
// undoes the product when the price write fails.
//
// Input contract: { id, name, description, images, stock, categoryIds, price, cost },
// where stock/price/cost arrive as strings — DynamoDB's wire format requires N values
// to be JSON strings, so the resolver stringifies them and the tasks map them with
// numberFromString. `images` arrives already in DynamoDB attribute-value JSON
// ([{ M: { ImageId: { S }, IsMain: { BOOL }, Order: { N } } }]) because
// listFromJsonPath passes the path through verbatim as the L value (ADR-0034).
export class ProductCreateSaga extends Construct {
  public readonly stateMachine: sfn.StateMachine;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Imported by table name (no CF coupling), same convention as appsync-api.ts.
    const productsTable = dynamodb.Table.fromTableName(this, 'ProductsTable', 'products');
    const pricesTable = dynamodb.Table.fromTableName(this, 'PricesTable', 'prices');

    // resultPath DISCARD on every write keeps '$' = the saga input, so later states
    // (and the compensation branch) still see $.id.
    const putProduct = new tasks.DynamoPutItem(this, 'PutProduct', {
      table: productsTable,
      item: {
        Id: tasks.DynamoAttributeValue.fromString(sfn.JsonPath.stringAt('$.id')),
        Name: tasks.DynamoAttributeValue.fromString(sfn.JsonPath.stringAt('$.name')),
        Description: tasks.DynamoAttributeValue.fromString(sfn.JsonPath.stringAt('$.description')),
        Images: tasks.DynamoAttributeValue.listFromJsonPath(sfn.JsonPath.stringAt('$.images')),
        Stock: tasks.DynamoAttributeValue.numberFromString(sfn.JsonPath.stringAt('$.stock')),
        CategoryIds: tasks.DynamoAttributeValue.fromStringSet(sfn.JsonPath.listAt('$.categoryIds')),
      },
      resultPath: sfn.JsonPath.DISCARD,
    });

    // Same upsert expression as the setNominalPrice direct resolver, so both write paths
    // produce identical items (and the same PriceChangedEvent CDC off the prices stream).
    const setPrice = new tasks.DynamoUpdateItem(this, 'SetNominalPrice', {
      table: pricesTable,
      key: {
        ProductId: tasks.DynamoAttributeValue.fromString(sfn.JsonPath.stringAt('$.id')),
      },
      updateExpression: 'SET NominalPrice = :nominalPrice, Cost = :cost, UpdatedAt = :now',
      expressionAttributeValues: {
        ':nominalPrice': tasks.DynamoAttributeValue.numberFromString(sfn.JsonPath.stringAt('$.price')),
        ':cost': tasks.DynamoAttributeValue.numberFromString(sfn.JsonPath.stringAt('$.cost')),
        ':now': tasks.DynamoAttributeValue.fromString(sfn.JsonPath.stringAt('$$.State.EnteredTime')),
      },
      resultPath: sfn.JsonPath.DISCARD,
    });

    const compensateDelete = new tasks.DynamoDeleteItem(this, 'CompensateDeleteProduct', {
      table: productsTable,
      key: {
        Id: tasks.DynamoAttributeValue.fromString(sfn.JsonPath.stringAt('$.id')),
      },
      resultPath: sfn.JsonPath.DISCARD,
    });

    const fail = new sfn.Fail(this, 'PriceWriteFailed', {
      error: 'PriceWriteFailed',
      causePath: sfn.JsonPath.stringAt('$.error.Cause'),
    });

    setPrice.addCatch(compensateDelete.next(fail), { resultPath: '$.error' });

    // Execution output becomes the mutation result — { id } only.
    const formatOutput = new sfn.Pass(this, 'FormatOutput', {
      parameters: { id: sfn.JsonPath.stringAt('$.id') },
    });

    this.stateMachine = new sfn.StateMachine(this, 'StateMachine', {
      stateMachineName: 'product-create-saga',
      stateMachineType: sfn.StateMachineType.EXPRESS,
      definitionBody: sfn.DefinitionBody.fromChainable(
        putProduct.next(setPrice).next(formatOutput),
      ),
      timeout: cdk.Duration.seconds(10),
      logs: {
        destination: new logs.LogGroup(this, 'Logs', {
          logGroupName: '/aws/states/product-create-saga',
          retention: logs.RetentionDays.ONE_WEEK,
          removalPolicy: cdk.RemovalPolicy.DESTROY,
        }),
        level: sfn.LogLevel.ERROR,
      },
    });
  }
}
