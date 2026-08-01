using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Challenges.Function.Modules.Progress.Data;
using Challenges.Function.Modules.Questions.Data;

namespace Challenges.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureChallengesTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureChallengesTableAsync(dynamoDb);
        await EnsureChallengeProgressTableAsync(dynamoDb);
    }

    private static async Task EnsureChallengeProgressTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = ProgressSchema.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition("OwnerId", ScalarAttributeType.S),
                    new AttributeDefinition("SK", ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement("OwnerId", KeyType.HASH),
                    new KeySchemaElement("SK", KeyType.RANGE)
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
                // Drives challenges-progress-stream-publisher (ADR-0045 §9, CH-07). NEW_AND_OLD
                // is the same view type every other rule-based publisher in this codebase uses.
                StreamSpecification = new StreamSpecification
                {
                    StreamEnabled = true,
                    StreamViewType = StreamViewType.NEW_AND_OLD_IMAGES
                }
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, ProgressSchema.TableName);
        }
        catch (ResourceInUseException)
        {
            // Table already exists — idempotent.
        }
    }

    private static async Task EnsureChallengesTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = ChallengesSchema.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition("QuestionId", ScalarAttributeType.S),
                    new AttributeDefinition("SK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1PK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1SK", ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement("QuestionId", KeyType.HASH),
                    new KeySchemaElement("SK", KeyType.RANGE)
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        // Sparse by construction: only the PUBLIC item carries GSI1PK/GSI1SK, so
                        // the ANSWER item is never indexed here (ADR-0045 §2).
                        IndexName = ChallengesSchema.Gsi1Name,
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

            await WaitUntilTableIsActiveAsync(dynamoDb, ChallengesSchema.TableName);
        }
        catch (ResourceInUseException)
        {
            // Table already exists — idempotent.
        }
    }

    internal static async Task WaitUntilTableIsActiveAsync(IAmazonDynamoDB dynamoDb, string tableName)
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
