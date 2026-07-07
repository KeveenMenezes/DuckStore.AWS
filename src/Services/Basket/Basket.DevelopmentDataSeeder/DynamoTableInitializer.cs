using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Basket.Function.Modules.ShoppingCarts.Data;

namespace Basket.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureBasketTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureShoppingCartsTableAsync(dynamoDb);
    }

    private static async Task EnsureShoppingCartsTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = DynamoShoppingCartRepository.TableName,
                AttributeDefinitions = [new AttributeDefinition("OwnerId", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("OwnerId", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST,
                StreamSpecification = new StreamSpecification
                {
                    StreamEnabled = true,
                    StreamViewType = StreamViewType.NEW_IMAGE
                }
            });

            // Guest carts carry an ExpiresAt attribute; TTL lets DynamoDB reap them after 15 days.
            await WaitUntilTableIsActiveAsync(dynamoDb, DynamoShoppingCartRepository.TableName);
            await EnableExpiresAtTtlAsync(dynamoDb);
        }
        catch (ResourceInUseException)
        {
            // Table already exists; ensure streams are enabled for CDC publishing.
            try
            {
                await dynamoDb.UpdateTableAsync(new UpdateTableRequest
                {
                    TableName = DynamoShoppingCartRepository.TableName,
                    StreamSpecification = new StreamSpecification
                    {
                        StreamEnabled = true,
                        StreamViewType = StreamViewType.NEW_IMAGE
                    }
                });
            }
            catch (ResourceInUseException)
            {
                // Streams already enabled — idempotent.
            }

            await EnableExpiresAtTtlAsync(dynamoDb);
        }
    }

    private static async Task EnableExpiresAtTtlAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
            {
                TableName = DynamoShoppingCartRepository.TableName,
                TimeToLiveSpecification = new TimeToLiveSpecification
                {
                    Enabled = true,
                    AttributeName = "ExpiresAt"
                }
            });
        }
        catch (Exception)
        {
            // TTL already enabled on ExpiresAt — idempotent.
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
