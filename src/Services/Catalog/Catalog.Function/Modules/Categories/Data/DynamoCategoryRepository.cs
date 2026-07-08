namespace Catalog.Function.Modules.Categories.Data;

// Table has only a PK (Id), no GSI — hierarchy lookups (children/descendants) go through
// ScanAsync + FilterExpression, acceptable at this demo dataset's scale. A GSI1(ParentId) would be
// the production fix if the category count grew large enough for a full scan to matter.
public class DynamoCategoryRepository(IAmazonDynamoDB dynamoDb) : ICategoryRepository
{
    public const string TableName = "categories";

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? MapCategory(response.Item) : null;
    }

    public async Task<List<Category>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = ids.Distinct()
            .Select(id => new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) })
            .ToList();

        if (keys.Count == 0)
            return [];

        var response = await dynamoDb.BatchGetItemAsync(
            new BatchGetItemRequest
            {
                RequestItems = new Dictionary<string, KeysAndAttributes>
                {
                    [TableName] = new() { Keys = keys }
                }
            },
            cancellationToken);

        return [.. response.Responses[TableName].Select(MapCategory)];
    }

    public async Task<List<Category>> ListAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName }, cancellationToken);

        return [.. response.Items.Select(MapCategory)];
    }

    public async Task<List<Category>> ListChildrenAsync(
        Guid parentId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest
            {
                TableName = TableName,
                FilterExpression = "ParentId = :parentId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":parentId"] = new(parentId.ToString())
                }
            },
            cancellationToken);

        return [.. response.Items.Select(MapCategory)];
    }

    public async Task<List<Category>> ListDescendantsAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest
            {
                TableName = TableName,
                FilterExpression = "contains(#path, :id)",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#path"] = "Path" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":id"] = new(id.ToString())
                }
            },
            cancellationToken);

        return [.. response.Items.Select(MapCategory)];
    }

    public Task AddAsync(Category category, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(category) },
            cancellationToken);

    public Task UpdateAsync(Category category, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(category) },
            cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
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

    private static Category MapCategory(Dictionary<string, AttributeValue> item) =>
        Category.Load(
            Guid.Parse(item["Id"].S),
            item["Name"].S,
            item.TryGetValue("ParentId", out var parentId) ? Guid.Parse(parentId.S) : null,
            (item.TryGetValue("Path", out var path) ? path.L : [])
                .Select(p => CategoryId.Of(Guid.Parse(p.S)))
                .ToList());
}
