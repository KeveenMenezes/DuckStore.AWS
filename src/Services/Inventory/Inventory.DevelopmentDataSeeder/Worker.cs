using System.Diagnostics;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Inventory.DevelopmentDataSeeder;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = s_activitySource.StartActivity(
            "Provisioning Inventory DynamoDB tables", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            await scope.ServiceProvider.EnsureInventoryTablesCreatedAsync();

            var dynamoDb = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
            foreach (var warehouse in InventoryInitialData.Warehouses)
                await SeedWarehouseAsync(dynamoDb, warehouse, stoppingToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    // Same write as Mutation.createWarehouse.js: the warehouse and its code guard together, both
    // conditioned on absence, so a restart cancels the transaction instead of duplicating anything.
    private static async Task SeedWarehouseAsync(IAmazonDynamoDB dynamoDb, SeedWarehouse warehouse, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("O");
        var item = new Dictionary<string, AttributeValue>
        {
            [WarehousesSchema.PartitionKey] = new() { S = warehouse.Id },
            ["Type"] = new() { S = WarehousesSchema.WarehouseType },
            ["Code"] = new() { S = warehouse.Code },
            ["Name"] = new() { S = warehouse.Name },
            ["Street"] = new() { S = warehouse.Street },
            ["Number"] = new() { S = warehouse.Number },
            ["District"] = new() { S = warehouse.District },
            ["City"] = new() { S = warehouse.City },
            ["State"] = new() { S = warehouse.State },
            ["ZipCode"] = new() { S = warehouse.ZipCode },
            ["Priority"] = new() { N = warehouse.Priority.ToString() },
            ["Active"] = new() { BOOL = true },
            ["CreatedAt"] = new() { S = now },
            ["UpdatedAt"] = new() { S = now },
            ["UpdatedBy"] = new() { S = "seeder" },
        };
        var guard = new Dictionary<string, AttributeValue>
        {
            [WarehousesSchema.PartitionKey] = new() { S = WarehousesSchema.CodeGuardKey(warehouse.Code) },
            ["Type"] = new() { S = WarehousesSchema.CodeGuardType },
            ["TargetWarehouseId"] = new() { S = warehouse.Id },
        };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    PutIfAbsent(item),
                    PutIfAbsent(guard),
                ],
            }, ct);
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons.All(reason => reason.Code is "None" or "ConditionalCheckFailed"))
        {
            // Already seeded. Any other cancellation reason (a conflict, a validation error) is real.
        }
    }

    private static TransactWriteItem PutIfAbsent(Dictionary<string, AttributeValue> item) => new()
    {
        Put = new Put
        {
            TableName = WarehousesSchema.TableName,
            Item = item,
            ConditionExpression = $"attribute_not_exists({WarehousesSchema.PartitionKey})",
        },
    };
}
