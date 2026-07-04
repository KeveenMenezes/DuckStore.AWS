using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;

namespace Ordering.FunctionalTests;

// Ordering exposes no HTTP API anymore — ordersByCustomer/deleteOrder are AppSync direct DynamoDB
// resolvers (ADR-0009) and the remaining Lambdas are event-driven. So this fixture boots the full
// Aspire graph, waits for the dev seeder to provision + seed the `ordering` table, and hands tests
// a DynamoDB Local client to exercise the same operations the resolvers perform (GSI1 query, DeleteItem).
public sealed class OrderingApiFixture : IAsyncLifetime
{
    // The full graph starts containers (DynamoDB Local, Elasticsearch), Lambdas, and the seeder.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    // DynamoDB Local is published on a fixed host port by the AppHost (WithHttpEndpoint(8000, 8000)).
    private const string DynamoDbUrl = "http://localhost:8000";

    public const string OrderingTable = "ordering";

    // Seeded by Ordering.DevelopmentDataSeeder (OrderingInitialData): order "ORD_1".
    public const string SeededOrderId = "194ea999-cd0b-498d-9760-dddf0d74cd2f";
    public const string SeededCustomerId = "58c49479-ec65-4de2-86e7-033c546291aa";

    private DistributedApplication _app = null!;

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AppHost>(["DcpPublisher:RandomizePorts=false"]);

        _app = await appHost.BuildAsync(cts.Token);
        await _app.StartAsync(cts.Token);

        DynamoDb = new AmazonDynamoDBClient(
            new BasicAWSCredentials("dummy", "dummy"),
            new AmazonDynamoDBConfig { ServiceURL = DynamoDbUrl, AuthenticationRegion = "us-east-1" });

        await WaitForSeededOrderAsync(cts.Token);
    }

    // Poll until the seeder has created the table and written ORD_1 — robust to container/seed
    // startup ordering, without depending on Aspire resource state names.
    private async Task WaitForSeededOrderAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await DynamoDb.GetItemAsync(
                    new GetItemRequest
                    {
                        TableName = OrderingTable,
                        Key = new Dictionary<string, AttributeValue> { ["Id"] = new(SeededOrderId) }
                    },
                    cancellationToken);

                if (response.IsItemSet)
                    return;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Table not created yet / DynamoDB Local not ready — retry until the timeout.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    public Task DisposeAsync()
    {
        DynamoDb?.Dispose();
        _app?.Dispose();

        return Task.CompletedTask;
    }
}
