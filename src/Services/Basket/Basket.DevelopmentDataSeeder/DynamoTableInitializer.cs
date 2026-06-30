using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Basket.Function.Data;
using Basket.Function.Models;

namespace Basket.DevelopmentDataSeeder;

public static class DynamoTableInitializer
{
    public static async Task EnsureBasketTablesCreatedAsync(this IServiceProvider services)
    {
        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureShoppingCartsTableAsync(dynamoDb);
        await EnsureCouponsTableAsync(dynamoDb);

        using var scope = services.CreateScope();
        var couponRepository = scope.ServiceProvider.GetRequiredService<ICouponRepository>();

        if (!await couponRepository.AnyAsync())
            await SeedCouponsAsync(couponRepository);
    }

    private static async Task EnsureShoppingCartsTableAsync(IAmazonDynamoDB dynamoDb)
    {
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

    private static async Task EnsureCouponsTableAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = DynamoCouponRepository.TableName,
                AttributeDefinitions = [new AttributeDefinition("ProductName", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("ProductName", KeyType.HASH)],
                BillingMode = BillingMode.PAY_PER_REQUEST
            });

            await WaitUntilTableIsActiveAsync(dynamoDb);
        }
        catch (ResourceInUseException)
        {
            // Table already exists.
        }
    }

    private static async Task WaitUntilTableIsActiveAsync(IAmazonDynamoDB dynamoDb)
    {
        while (true)
        {
            var response = await dynamoDb.DescribeTableAsync(DynamoCouponRepository.TableName);

            if (response.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static async Task SeedCouponsAsync(ICouponRepository couponRepository)
    {
        var coupons = new[]
        {
            new Coupon { Id = "IPhone X", Description = "IPhone Description X", Amount = 1 },
            new Coupon { Id = "IPhone XI", Description = "IPhone Description XI", Amount = 2 }
        };

        foreach (var coupon in coupons)
        {
            await couponRepository.AddAsync(coupon);
        }
    }
}
