using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Discount.Grpc.Data;

public class DynamoCouponRepository(IAmazonDynamoDB dynamoDb) : ICouponRepository
{
    public const string TableName = "Coupons";

    public async Task<Coupon?> GetByProductNameAsync(string productName, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductName"] = new(productName) }
            },
            cancellationToken);

        return response.Item.Count == 0 ? null : ToCoupon(response.Item);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    public Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(new PutItemRequest { TableName = TableName, Item = ToItem(coupon) }, cancellationToken);

    public Task UpdateAsync(Coupon coupon, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(new PutItemRequest { TableName = TableName, Item = ToItem(coupon) }, cancellationToken);

    public Task DeleteAsync(string productName, CancellationToken cancellationToken = default) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["ProductName"] = new(productName) }
            },
            cancellationToken);

    private static Dictionary<string, AttributeValue> ToItem(Coupon coupon) =>
        new()
        {
            ["ProductName"] = new(coupon.ProductName),
            ["Description"] = new(coupon.Description),
            ["Amount"] = new AttributeValue { N = coupon.Amount.ToString() }
        };

    private static Coupon ToCoupon(Dictionary<string, AttributeValue> item) =>
        new()
        {
            ProductName = item["ProductName"].S,
            Description = item["Description"].S,
            Amount = int.Parse(item["Amount"].N)
        };
}
