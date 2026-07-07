namespace Pricing.Function.Modules.GatewayCosts.Data;

// One item per provider, keyed by Provider — a single PutItem is atomic on its own. No Streams:
// the installment engine reads this synchronously wherever it needs it (GetInstallmentPlan,
// PriceStreamPublisher); a gateway-cost change alone never fans out to CatalogView (ADR-0028 —
// badges only refresh on the next price change or a manual backfill).
public class DynamoGatewayCostRepository(IAmazonDynamoDB dynamoDb) : IGatewayCostRepository
{
    public const string TableName = "gateway-costs";

    public async Task<GatewayCost?> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Provider"] = new(provider) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? MapGatewayCost(response.Item) : null;
    }

    public Task PutAsync(GatewayCost gatewayCost, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(gatewayCost) },
            cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    private static Dictionary<string, AttributeValue> ToItem(GatewayCost gatewayCost) =>
        new()
        {
            ["Provider"] = new(gatewayCost.Id.Value),
            ["FlatFeePerTransaction"] = new AttributeValue
            {
                N = gatewayCost.FlatFeePerTransaction.ToString(CultureInfo.InvariantCulture)
            },
            ["AvistaRatePercent"] = new AttributeValue
            {
                N = gatewayCost.AvistaRatePercent.ToString(CultureInfo.InvariantCulture)
            },
            ["InstallmentRates"] = new AttributeValue
            {
                M = gatewayCost.InstallmentRates.ToDictionary(
                    rate => rate.Key.ToString(CultureInfo.InvariantCulture),
                    rate => new AttributeValue { N = rate.Value.ToString(CultureInfo.InvariantCulture) })
            },
            ["UpdatedAt"] = new(
                (gatewayCost.LastModified ?? gatewayCost.CreatedAt ?? DateTime.UtcNow).ToString("o"))
        };

    private static GatewayCost MapGatewayCost(Dictionary<string, AttributeValue> item) =>
        GatewayCost.Load(
            item["Provider"].S,
            decimal.Parse(item["FlatFeePerTransaction"].N, CultureInfo.InvariantCulture),
            decimal.Parse(item["AvistaRatePercent"].N, CultureInfo.InvariantCulture),
            item["InstallmentRates"].M.ToDictionary(
                rate => int.Parse(rate.Key, CultureInfo.InvariantCulture),
                rate => decimal.Parse(rate.Value.N, CultureInfo.InvariantCulture)),
            DateTime.Parse(item["UpdatedAt"].S, null, DateTimeStyles.RoundtripKind));
}
