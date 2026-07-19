using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Review.Function.Modules.Reviews.Data;

namespace Review.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureReviewTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureReviewTableAsync(dynamoDb);
    }

    private static async Task EnsureReviewTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = ReviewSchema.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1PK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1SK", ScalarAttributeType.S)
                ],
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        // GSI1 lists reviews by product (GSI1PK=ProductId, GSI1SK=CreatedAt).
                        // Projection ALL avoids an extra GetItem per result on read.
                        IndexName = ReviewSchema.Gsi1Name,
                        KeySchema =
                        [
                            new KeySchemaElement("GSI1PK", KeyType.HASH),
                            new KeySchemaElement("GSI1SK", KeyType.RANGE)
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    }
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
                // Streams drive the rule-based ReviewStreamPublisher (ADR-0011/ADR-0019/ADR-0029).
                // NEW_AND_OLD_IMAGES lets ReviewUpdatedRule diff the old/new rating on a MODIFY.
                StreamSpecification = new StreamSpecification
                {
                    StreamEnabled = true,
                    StreamViewType = StreamViewType.NEW_AND_OLD_IMAGES
                }
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, ReviewSchema.TableName);
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
