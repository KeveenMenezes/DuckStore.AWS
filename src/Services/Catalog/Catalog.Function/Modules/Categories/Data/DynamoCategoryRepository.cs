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

        // BatchGetItem caps at 100 keys per call, and can return UnprocessedKeys under
        // throttling — retry those rather than silently dropping categories from the result.
        const int batchSize = 100;
        var items = new List<Dictionary<string, AttributeValue>>();

        foreach (var chunk in keys.Chunk(batchSize))
        {
            var requestItems = new Dictionary<string, KeysAndAttributes>
            {
                [TableName] = new() { Keys = [.. chunk] }
            };

            while (requestItems.Count > 0)
            {
                var response = await dynamoDb.BatchGetItemAsync(
                    new BatchGetItemRequest { RequestItems = requestItems }, cancellationToken);

                if (response.Responses.TryGetValue(TableName, out var batch))
                    items.AddRange(batch);

                requestItems = response.UnprocessedKeys is { Count: > 0 } ? response.UnprocessedKeys : [];
            }
        }

        return [.. items.Select(MapCategory)];
    }

    public async Task<List<Category>> ListAsync(CancellationToken cancellationToken = default) =>
        await ScanAllAsync(new ScanRequest { TableName = TableName }, cancellationToken);

    // Scans have no GSI to lean on at this table's scale (see class comment), but must still
    // follow LastEvaluatedKey — a single unpaginated Scan silently truncates once results exceed
    // DynamoDB's 1MB page limit.
    private async Task<List<Category>> ScanAllAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            request.ExclusiveStartKey = lastKey;
            var response = await dynamoDb.ScanAsync(request, cancellationToken);
            items.AddRange(response.Items);

            lastKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        } while (lastKey is not null);

        return [.. items.Select(MapCategory)];
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

    private static Category MapCategory(Dictionary<string, AttributeValue> item) =>
        Category.Load(
            Guid.Parse(item["Id"].S),
            item["Name"].S,
            item.TryGetValue("ParentId", out var parentId) ? Guid.Parse(parentId.S) : null,
            (item.TryGetValue("Path", out var path) ? path.L : [])
                .Select(p => CategoryId.Of(Guid.Parse(p.S)))
                .ToList());
}
