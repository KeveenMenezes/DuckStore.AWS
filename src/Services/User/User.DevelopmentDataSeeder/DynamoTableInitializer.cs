using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using User.Function.Modules.Users.Data;

namespace User.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureUserTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureTableAsync(dynamoDb, DynamoUserProfileRepository.TableName, "UserId");
    }

    private static async Task EnsureTableAsync(
        IAmazonDynamoDB dynamoDb, string tableName, string partitionKey)
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
