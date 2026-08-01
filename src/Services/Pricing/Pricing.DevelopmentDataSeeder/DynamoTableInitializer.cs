using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.CustomerDiscounts.Data;
using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.Prices.Data;
using Pricing.Function.Shared.Data;

namespace Pricing.Function.Shared.Data;

public static class DynamoTableInitializer
{
    public static async Task EnsurePricingTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        // Streams on prices drive the PriceChanged CDC publisher (CatalogView price sync).
        await EnsureTableAsync(dynamoDb, DynamoPriceRepository.TableName, "ProductId", withStream: true);
        await EnsureTableAsync(dynamoDb, DynamoCampaignRepository.TableName, "Id");
        // Streams drive the ProductDiscountChanged CDC publisher so CatalogView refreshes the
        // catalog price when a campaign starts or ends (ADR-0044). TTL is not simulated by
        // DynamoDB Local, so a natural expiry only rolls back on real AWS.
        await EnsureTableAsync(
            dynamoDb, DynamoCampaignRepository.ProductDiscountsTableName, "ProductId",
            withStream: true, streamViewType: StreamViewType.KEYS_ONLY);
        // No stream: gateway-cost changes alone never fan out to CatalogView (ADR-0028).
        await EnsureTableAsync(dynamoDb, DynamoGatewayCostRepository.TableName, "Provider");
        await EnsureTableAsync(dynamoDb, ProcessedIntegrationEvent.TableName, "PK");
        // No stream: myRewards/GetBasketInstallmentPlan both read this table directly, and the
        // CDC-out side (if a discount ever needs to fan out) doesn't exist yet (ADR-0046 §4).
        await EnsureTableAsync(
            dynamoDb, DynamoCustomerDiscountRepository.TableName, "OwnerId", rangeKeyName: "DiscountId");
    }

    private static async Task EnsureTableAsync(
        IAmazonDynamoDB dynamoDb,
        string tableName,
        string hashKeyName,
        bool withStream = false,
        StreamViewType? streamViewType = null,
        string? rangeKeyName = null)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition(hashKeyName, ScalarAttributeType.S),
                    .. rangeKeyName is null
                        ? Array.Empty<AttributeDefinition>()
                        : [new AttributeDefinition(rangeKeyName, ScalarAttributeType.S)]
                ],
                KeySchema =
                [
                    new KeySchemaElement(hashKeyName, KeyType.HASH),
                    .. rangeKeyName is null
                        ? Array.Empty<KeySchemaElement>()
                        : [new KeySchemaElement(rangeKeyName, KeyType.RANGE)]
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
                StreamSpecification = withStream
                    ? new StreamSpecification
                    {
                        StreamEnabled = true,
                        StreamViewType = streamViewType ?? StreamViewType.NEW_IMAGE
                    }
                    : null
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
