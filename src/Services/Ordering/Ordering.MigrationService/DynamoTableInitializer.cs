using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.RepositoryAdapters;

namespace Ordering.MigrationService;

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
                TableName = OrderRepository.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition("PK", ScalarAttributeType.S),
                    new AttributeDefinition("SK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1PK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI1SK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI2PK", ScalarAttributeType.S),
                    new AttributeDefinition("GSI2SK", ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement("PK", KeyType.HASH),
                    new KeySchemaElement("SK", KeyType.RANGE)
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = OrderRepository.Gsi1Name,
                        KeySchema =
                        [
                            new KeySchemaElement("GSI1PK", KeyType.HASH),
                            new KeySchemaElement("GSI1SK", KeyType.RANGE)
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.KEYS_ONLY }
                    },
                    new GlobalSecondaryIndex
                    {
                        // GSI2 lista pedidos por status (GSI2PK=STATUS#{status}, GSI2SK=CreatedAt).
                        // Projection ALL evita um GetItem extra por resultado na leitura.
                        IndexName = OrderRepository.Gsi2Name,
                        KeySchema =
                        [
                            new KeySchemaElement("GSI2PK", KeyType.HASH),
                            new KeySchemaElement("GSI2SK", KeyType.RANGE)
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

            await WaitUntilTableIsActiveAsync(dynamoDb, OrderRepository.TableName);
        }
        catch (ResourceInUseException)
        {
            // Tabela já existe — idempotente.
        }
    }

    private static async Task EnsureProcessedIntegrationEventsTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = ProcessedIntegrationEvent.TableName,
                AttributeDefinitions = [new AttributeDefinition("MessageId", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("MessageId", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });

            await WaitUntilTableIsActiveAsync(dynamoDb, ProcessedIntegrationEvent.TableName);
        }
        catch (ResourceInUseException)
        {
            // Tabela já existe — idempotente.
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
