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
                    new AttributeDefinition(ReviewSchema.Gsi1PkAttribute, ScalarAttributeType.S),
                    new AttributeDefinition(ReviewSchema.Gsi1SkAttribute, ScalarAttributeType.S)
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
                            new KeySchemaElement(ReviewSchema.Gsi1PkAttribute, KeyType.HASH),
                            new KeySchemaElement(ReviewSchema.Gsi1SkAttribute, KeyType.RANGE)
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

        // Outside the try: a table created before ADR-0049 also needs TTL turned on.
        await EnableExpiresAtTtlAsync(dynamoDb);
    }

    // Deleted reviews carry ExpiresAt (epoch seconds); TTL removes the row 5 days after the
    // delete (ADR-0049 §7). The TTL REMOVE reaches the stream but publishes nothing.
    // Checked first instead of catching the "already enabled" error, so any other failure
    // (wrong table, wrong attribute) still stops the seeder.
    private static async Task EnableExpiresAtTtlAsync(IAmazonDynamoDB dynamoDb)
    {
        var current = await dynamoDb.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest
        {
            TableName = ReviewSchema.TableName
        });

        if (current.TimeToLiveDescription?.TimeToLiveStatus == TimeToLiveStatus.ENABLED)
            return;

        await dynamoDb.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
        {
            TableName = ReviewSchema.TableName,
            TimeToLiveSpecification = new TimeToLiveSpecification
            {
                Enabled = true,
                AttributeName = ReviewSchema.ExpiresAtAttribute
            }
        });
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
