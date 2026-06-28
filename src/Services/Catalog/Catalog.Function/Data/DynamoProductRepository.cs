using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Catalog.Function.Data;

public class DynamoProductRepository(IAmazonDynamoDB dynamoDb) : IProductRepository
{
    public const string TableName = "products";

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? ToProduct(response.Item) : null;
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest
            {
                TableName = TableName,
                FilterExpression = "contains(CategoryIds, :categoryId)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":categoryId"] = new(categoryId.ToString())
                }
            },
            cancellationToken);

        return [.. (response.Items ?? [])
            .Select(ToProduct)];
    }

    public async Task<PaginatedResult<Product>> GetPagedAsync(
        int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName },
            cancellationToken);

        var items = response.Items ?? [];

        var page = items
            .Skip((Math.Max(pageIndex, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(ToProduct)
            .ToList();

        return new PaginatedResult<Product>(
            pageIndex,
            pageSize,
            items.Count,
            page);
    }

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

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(product) },
            cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
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

    private static Product ToProduct(Dictionary<string, AttributeValue> item) =>
        Product.Load(
            Guid.Parse(item["Id"].S),
            item["Name"].S,
            item["Description"].S,
            item["ImageUrl"].S,
            decimal.Parse(item["Price"].N, CultureInfo.InvariantCulture),
            int.Parse(item["Stock"].N, CultureInfo.InvariantCulture),
            CategoryId.Of(item["CategoryIds"].SS.Select(Guid.Parse)),
            // Older products predate ratings — default to 0 when the attributes are absent.
            item.TryGetValue("AverageRating", out var avg)
                ? double.Parse(avg.N, CultureInfo.InvariantCulture)
                : 0,
            item.TryGetValue("RatingCount", out var count)
                ? int.Parse(count.N, CultureInfo.InvariantCulture)
                : 0);
}
