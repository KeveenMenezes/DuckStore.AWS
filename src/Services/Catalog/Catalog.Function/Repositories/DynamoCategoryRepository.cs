using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Catalog.Function.Repositories;

public class DynamoCategoryRepository(IAmazonDynamoDB dynamoDb) : ICategoryRepository
{
    public const string TableName = "Categories";

    public async Task<PaginatedResult<Category>> GetPagedAsync(
        int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName },
            cancellationToken);

        var page = response.Items
            .Skip((Math.Max(pageIndex, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(ToCategory)
            .ToList();

        return new PaginatedResult<Category>(pageIndex, pageSize, response.Items.Count, page);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    public Task AddAsync(Category category, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(category) },
            cancellationToken);

    private static Dictionary<string, AttributeValue> ToItem(Category category)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(category.Id.Value.ToString()),
            ["Name"] = new(category.Name),
            ["Path"] = new AttributeValue
            {
                L = [.. category.Path.Select(p => new AttributeValue(p.Value.ToString()))]
            }
        };

        if (category.ParentId is not null)
            item["ParentId"] = new AttributeValue(category.ParentId.Value.ToString());

        return item;
    }

    private static Category ToCategory(Dictionary<string, AttributeValue> item) =>
        Category.Load(
            Guid.Parse(item["Id"].S),
            item["Name"].S,
            item.TryGetValue("ParentId", out var parentId) ? Guid.Parse(parentId.S) : null,
            [.. item["Path"].L.Select(p => CategoryId.Of(Guid.Parse(p.S)))]);
}
