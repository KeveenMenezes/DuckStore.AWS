using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Catalog.Function.Modules.Products.Data;

public class DynamoProductRepository(IAmazonDynamoDB dynamoDb) : IProductRepository
{
    public const string TableName = "products";

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    public Task AddAsync(Product product, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(product) },
            cancellationToken);

    // Rating fields no longer live here — they are materialized exclusively in CatalogView's
    // index (ADR-0027, supersedes ADR-0011 §4; ADR-0030).
    private static Dictionary<string, AttributeValue> ToItem(Product product) =>
        new()
        {
            ["Id"] = new(product.Id.Value.ToString()),
            ["Name"] = new(product.Name),
            ["Description"] = new(product.Description),
            // List of maps — the same shape the AppSync resolvers and the create saga write
            // (ADR-0034), so every producer of a product item is stream-compatible.
            ["Images"] = new AttributeValue
            {
                L = [.. product.Images.Select(i => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["ImageId"] = new(i.ImageId),
                        ["IsMain"] = new AttributeValue { BOOL = i.IsMain },
                        ["Order"] = new AttributeValue { N = i.Order.ToString(CultureInfo.InvariantCulture) }
                    }
                })],
                IsLSet = true
            },
            ["Stock"] = new AttributeValue { N = product.Stock.ToString(CultureInfo.InvariantCulture) },
            ["CategoryIds"] = new AttributeValue
            {
                SS = [.. product.CategoryIds.Select(c => c.Value.ToString())]
            }
        };
}
