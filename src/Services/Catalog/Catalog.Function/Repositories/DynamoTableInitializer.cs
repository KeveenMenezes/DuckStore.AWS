using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Function.Repositories;

public static class DynamoTableInitializer
{
    public static async Task EnsureCatalogTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureTableAsync(dynamoDb, DynamoProductRepository.TableName);
        await EnsureTableAsync(dynamoDb, DynamoCategoryRepository.TableName);
    }

    private static async Task EnsureTableAsync(IAmazonDynamoDB dynamoDb, string tableName)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions = [new AttributeDefinition("Id", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });
        }
        catch (ResourceInUseException)
        {
            // Table already exists — idempotent.
        }
    }
}
