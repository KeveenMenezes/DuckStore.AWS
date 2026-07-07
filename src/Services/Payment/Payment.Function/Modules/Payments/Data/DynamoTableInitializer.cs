using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Payment.Function.Shared.Data;

namespace Payment.Function.Modules.Payments.Data;

public static class DynamoTableInitializer
{
    public static async Task EnsurePaymentTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsurePaymentsTableAsync(dynamoDb);
        await EnsureProcessedIntegrationEventsTableAsync(dynamoDb);
    }

    private static async Task EnsurePaymentsTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = DynamoPaymentRepository.TableName,
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
                        // GSI1 lists payments by order (GSI1PK=ORDER#{id}, GSI1SK=CreatedAt).
                        // Projection ALL avoids an extra GetItem per result on read.
                        IndexName = DynamoPaymentRepository.Gsi1Name,
                        KeySchema =
                        [
                            new KeySchemaElement("GSI1PK", KeyType.HASH),
                            new KeySchemaElement("GSI1SK", KeyType.RANGE)
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    }
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
                StreamSpecification = new StreamSpecification
                {
                    StreamEnabled = true,
                    // NEW_AND_OLD_IMAGES so stream-publisher rules can detect transitions by
                    // comparing the old and new images (see ADR-0019).
                    StreamViewType = StreamViewType.NEW_AND_OLD_IMAGES
                }
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, DynamoPaymentRepository.TableName);
        }
        catch (ResourceInUseException)
        {
            // Table already exists — idempotent.
        }
    }

    private static async Task EnsureProcessedIntegrationEventsTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = ProcessedIntegrationEvent.TableName,
                AttributeDefinitions = [new AttributeDefinition("PK", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("PK", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, ProcessedIntegrationEvent.TableName);
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
