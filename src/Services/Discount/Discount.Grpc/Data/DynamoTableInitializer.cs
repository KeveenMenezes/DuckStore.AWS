using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Discount.Grpc.Data;

public static class DynamoTableInitializer
{
    public static async Task EnsureDiscountTableCreatedAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var dynamoDb = services.GetRequiredService<IAmazonDynamoDB>();

        await EnsureTableExistsAsync(dynamoDb);

        using var scope = services.CreateScope();

        var couponRepository = scope.ServiceProvider.GetRequiredService<ICouponRepository>();

        if (await couponRepository.AnyAsync())
            return;

        await SeedDataAsync(couponRepository);
    }

    private static async Task EnsureTableExistsAsync(IAmazonDynamoDB dynamoDb)
    {
        try
        {
            await dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = DynamoCouponRepository.TableName,
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "ProductName",
                        ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement(
                        "ProductName",
                        KeyType.HASH)
                ],
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
            var response = await dynamoDb.DescribeTableAsync(
                DynamoCouponRepository.TableName);

            if (response.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static async Task SeedDataAsync(ICouponRepository couponRepository)
    {
        var coupons = new[]
        {
            new Coupon
            {
                ProductName = "IPhone X",
                Description = "IPhone Description X",
                Amount = 1
            },
            new Coupon
            {
                ProductName = "IPhone XI",
                Description = "IPhone Description XI",
                Amount = 2
            }
        };

        foreach (var coupon in coupons)
        {
            await couponRepository.AddAsync(coupon);
        }
    }
}
