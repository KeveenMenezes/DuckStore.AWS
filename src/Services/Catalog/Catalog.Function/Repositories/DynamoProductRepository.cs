using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Catalog.Function.Repositories;

public class DynamoProductRepository(IAmazonDynamoDB dynamoDb) : IProductRepository
{
    public const string TableName = "Products";

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
            cancellationToken);

        return response.Item.Count == 0 ? null : ToProduct(response.Item);
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

        return [.. response.Items.Select(ToProduct)];
    }

    public async Task<PaginatedResult<Product>> GetPagedAsync(
        int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName },
            cancellationToken);

        var page = response.Items
            .Skip((Math.Max(pageIndex, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(ToProduct)
            .ToList();

        return new PaginatedResult<Product>(pageIndex, pageSize, response.Items.Count, page);
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
            CategoryId.Of(item["CategoryIds"].SS.Select(Guid.Parse)));
}
