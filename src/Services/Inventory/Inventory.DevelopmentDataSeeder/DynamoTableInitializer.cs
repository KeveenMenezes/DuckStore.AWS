using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Inventory.Function.Modules.Stock.Data;
using Inventory.Function.Modules.Warehouses.Data;

namespace Inventory.DevelopmentDataSeeder;

// Mirrors infra/constructs/inventory-dynamodb.ts (ADR-0052 §2, §7). No streams.
public static class DynamoTableInitializer
{
    public static async Task EnsureInventoryTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await CreateIfAbsentAsync(dynamoDb, new CreateTableRequest
        {
            TableName = WarehousesSchema.TableName,
            AttributeDefinitions = [new(WarehousesSchema.PartitionKey, ScalarAttributeType.S)],
            KeySchema = [new(WarehousesSchema.PartitionKey, KeyType.HASH)],
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });

        // GSI1 lists a warehouse's balances.
        await CreateIfAbsentAsync(dynamoDb, new CreateTableRequest
        {
            TableName = StockSchema.TableName,
            AttributeDefinitions =
            [
                new(StockSchema.PartitionKey, ScalarAttributeType.S),
                new(StockSchema.SortKey, ScalarAttributeType.S),
            ],
            KeySchema =
            [
                new(StockSchema.PartitionKey, KeyType.HASH),
                new(StockSchema.SortKey, KeyType.RANGE),
            ],
            GlobalSecondaryIndexes =
            [
                new()
                {
                    IndexName = StockSchema.ByWarehouseIndex,
                    KeySchema =
                    [
                        new(StockSchema.SortKey, KeyType.HASH),
                        new(StockSchema.PartitionKey, KeyType.RANGE),
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                },
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });

        // LSI1 is a product's history by date, GSI1 a warehouse's history by date.
        await CreateIfAbsentAsync(dynamoDb, new CreateTableRequest
        {
            TableName = MovementsSchema.TableName,
            AttributeDefinitions =
            [
                new(MovementsSchema.PartitionKey, ScalarAttributeType.S),
                new(MovementsSchema.SortKey, ScalarAttributeType.S),
                new(MovementsSchema.CreatedAtAttribute, ScalarAttributeType.S),
                new(MovementsSchema.WarehouseIdAttribute, ScalarAttributeType.S),
            ],
            KeySchema =
            [
                new(MovementsSchema.PartitionKey, KeyType.HASH),
                new(MovementsSchema.SortKey, KeyType.RANGE),
            ],
            LocalSecondaryIndexes =
            [
                new()
                {
                    IndexName = MovementsSchema.ByCreatedAtIndex,
                    KeySchema =
                    [
                        new(MovementsSchema.PartitionKey, KeyType.HASH),
                        new(MovementsSchema.CreatedAtAttribute, KeyType.RANGE),
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                },
            ],
            GlobalSecondaryIndexes =
            [
                new()
                {
                    IndexName = MovementsSchema.ByWarehouseIndex,
                    KeySchema =
                    [
                        new(MovementsSchema.WarehouseIdAttribute, KeyType.HASH),
                        new(MovementsSchema.CreatedAtAttribute, KeyType.RANGE),
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                },
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });
    }

    private static async Task CreateIfAbsentAsync(IAmazonDynamoDB dynamoDb, CreateTableRequest request)
    {
        try
        {
            await dynamoDb.CreateTableAsync(request);
        }
        catch (ResourceInUseException)
        {
            // Table already exists — nothing to do.
        }
    }
}
