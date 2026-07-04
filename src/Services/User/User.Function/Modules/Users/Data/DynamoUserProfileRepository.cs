using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace User.Function.Modules.Users.Data;

public class DynamoUserProfileRepository(IAmazonDynamoDB dynamoDb) : IUserProfileRepository
{
    public const string TableName = "user-profiles";

    public async Task<UserProfile?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["UserId"] = new(userId) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? ToProfile(response.Item) : null;
    }

    public Task PutAsync(UserProfile profile, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(profile) },
            cancellationToken);

    private static Dictionary<string, AttributeValue> ToItem(UserProfile profile)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["UserId"] = new(profile.UserId),
            ["Email"] = new(profile.Email),
            ["Name"] = new(profile.Name)
        };

        // Optional fields are only written when present — DynamoDB rejects empty strings.
        AddIfPresent(item, "Phone", profile.Phone);
        AddIfPresent(item, "AddressLine", profile.AddressLine);
        AddIfPresent(item, "City", profile.City);
        AddIfPresent(item, "State", profile.State);
        AddIfPresent(item, "ZipCode", profile.ZipCode);
        AddIfPresent(item, "Country", profile.Country);

        return item;
    }

    private static void AddIfPresent(Dictionary<string, AttributeValue> item, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            item[key] = new AttributeValue(value);
    }

    private static UserProfile ToProfile(Dictionary<string, AttributeValue> item) =>
        UserProfile.Load(
            item["UserId"].S,
            item["Email"].S,
            item["Name"].S,
            Read(item, "Phone"),
            Read(item, "AddressLine"),
            Read(item, "City"),
            Read(item, "State"),
            Read(item, "ZipCode"),
            Read(item, "Country"));

    private static string? Read(Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? value.S : null;
}
