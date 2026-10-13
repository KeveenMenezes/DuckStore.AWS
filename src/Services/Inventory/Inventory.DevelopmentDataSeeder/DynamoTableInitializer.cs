using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Inventory.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    // Mirrors infra/constructs/inventory-dynamodb.ts: PK only, no indexes, no stream (ADR-0052 §2).
    public static async Task EnsureInventoryTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = WarehousesSchema.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition(WarehousesSchema.PartitionKey, ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement(WarehousesSchema.PartitionKey, KeyType.HASH)
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });
        }
        catch (ResourceInUseException)
        {
            // Table already exists — nothing to do.
        }
    }
}
