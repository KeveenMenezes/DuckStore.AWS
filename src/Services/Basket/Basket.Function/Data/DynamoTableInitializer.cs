using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Basket.Function.Data;

public static class DynamoTableInitializer
{
    public static async Task EnsureBasketTableCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = BasketRepository.TableName,
                AttributeDefinitions = [new AttributeDefinition("UserName", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("UserName", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST,
                StreamSpecification = new StreamSpecification
                {
                    StreamEnabled = true,
                    StreamViewType = StreamViewType.NEW_IMAGE
                }
            });
        }
        catch (ResourceInUseException)
        {
            // Table already exists; ensure streams are enabled for CDC publishing.
            try
            {
                await dynamoDb.UpdateTableAsync(new UpdateTableRequest
                {
                    TableName = BasketRepository.TableName,
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
        }
    }
}
