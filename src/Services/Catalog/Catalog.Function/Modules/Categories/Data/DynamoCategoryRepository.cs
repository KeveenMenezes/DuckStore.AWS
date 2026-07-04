using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Catalog.Function.Modules.Categories.Data;

public class DynamoCategoryRepository(IAmazonDynamoDB dynamoDb) : ICategoryRepository
{
    public const string TableName = "categories";

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
}
