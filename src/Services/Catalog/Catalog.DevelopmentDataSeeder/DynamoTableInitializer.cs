using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Catalog.Function.Modules.Categories.Data;
using Catalog.Function.Modules.Products.Data;
using Catalog.Function.Shared.Data;

namespace Catalog.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureCatalogTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureTableAsync(dynamoDb, DynamoProductRepository.TableName);
        await EnsureTableAsync(dynamoDb, DynamoCategoryRepository.TableName);
        // Inbox for the ReviewCreated consumer's idempotency (ADR-0011).
        await EnsureTableAsync(dynamoDb, ProcessedIntegrationEvent.TableName, partitionKey: "PK");
    }

    private static async Task EnsureTableAsync(
        IAmazonDynamoDB dynamoDb, string tableName, string partitionKey = "Id")
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions = [new AttributeDefinition(partitionKey, ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement(partitionKey, KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, tableName);
        }
        catch (ResourceInUseException)
        {
            // Table already exists — idempotent.
        }
    }

    private static async Task WaitUntilTableIsActiveAsync(IAmazonDynamoDB dynamoDb, string tableName)
    {
        while (true)
        {
            var response = await dynamoDb.DescribeTableAsync(tableName);

            if (response.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
