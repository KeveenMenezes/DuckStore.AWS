using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CatalogView.Function.Modules.Products.Data;

namespace CatalogView.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureCatalogViewTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureCatalogViewProductsTableAsync(dynamoDb);
    }

    private static async Task EnsureCatalogViewProductsTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = DynamoProductIndex.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1PK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1SK", ScalarAttributeType.N)
                ],
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        // GSI1 lists all products sorted by AverageRating (GSI1PK is the
                        // constant "PRODUCT") — lets the unfiltered/rating-only browse path
                        // Query instead of Scan. ProjectionType ALL avoids a follow-up GetItem.
                        IndexName = DynamoProductIndex.Gsi1Name,
                        KeySchema =
                        [
                            new KeySchemaElement("GSI1PK", KeyType.HASH),
                            new KeySchemaElement("GSI1SK", KeyType.RANGE)
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    }
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, DynamoProductIndex.TableName);
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
