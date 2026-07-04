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

    // Intentionally omits AverageRating/RatingCount/RatingSum: those are maintained by the
    // ReviewCreated consumer Lambda (ADR-0011). A full-item write here must never clobber them,
    // so they are left untouched — DynamoDB keeps the existing values on PutItem only for the
    // keys present; since product updates go through partial UpdateItem resolvers, the counters
    // survive regardless.
    private static Dictionary<string, AttributeValue> ToItem(Product product) =>
        new()
        {
            ["Id"] = new(product.Id.Value.ToString()),
            ["Name"] = new(product.Name),
            ["Description"] = new(product.Description),
            ["ImageUrl"] = new(product.ImageUrl),
            ["Price"] = new AttributeValue { N = product.Price.ToString(CultureInfo.InvariantCulture) },
            ["Stock"] = new AttributeValue { N = product.Stock.ToString(CultureInfo.InvariantCulture) },
            ["CategoryIds"] = new AttributeValue
            {
                SS = [.. product.CategoryIds.Select(c => c.Value.ToString())]
            }
        };
}
