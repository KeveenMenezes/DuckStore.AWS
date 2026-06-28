using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.Function.Data;

public static class DynamoTableInitializer
{
    public static async Task EnsureOrderingTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureOrderingTableAsync(dynamoDb);
        await EnsureProcessedIntegrationEventsTableAsync(dynamoDb);
    }

    private static async Task EnsureOrderingTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = DynamoOrderRepository.TableName,
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
                        // GSI1 lista pedidos por cliente (GSI1PK=CUSTOMER#{id}, GSI1SK=CreatedAt).
                        // Projection ALL evita um GetItem extra por resultado na leitura.
                        IndexName = DynamoOrderRepository.Gsi1Name,
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
                    StreamViewType = StreamViewType.NEW_IMAGE
                }
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, DynamoOrderRepository.TableName);
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
