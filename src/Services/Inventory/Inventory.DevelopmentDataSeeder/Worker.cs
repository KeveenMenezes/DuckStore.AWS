using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Inventory.Function.Modules.Stock.Data;
using Inventory.Function.Modules.Stock.Domain;
using Inventory.Function.Modules.Stock.Features;
using Inventory.Function.Modules.Stock.Features.RecordReceipt;
using Inventory.Function.Modules.Warehouses.Data;

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
            "Provisioning and seeding Inventory DynamoDB tables", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            await scope.ServiceProvider.EnsureInventoryTablesCreatedAsync();

            var dynamoDb = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
            foreach (var warehouse in InventoryInitialData.Warehouses)
                await SeedWarehouseAsync(dynamoDb, warehouse, stoppingToken);
            await SeedStockAsync(dynamoDb, stoppingToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    // Through the receipt handler, so every balance has its ledger row (ADR-0052 §7). The key is
    // derived from the product, so a restart replays the recorded receipt instead of adding another.
    private static async Task SeedStockAsync(IAmazonDynamoDB dynamoDb, CancellationToken ct)
    {
        var stock = new DynamoStockRepository(dynamoDb, TimeProvider.System);
        var receive = new RecordReceiptHandler(
            new MovementRecorder(stock, TimeProvider.System), new DynamoWarehouseReader(dynamoDb), stock);
        var totals = new DynamoStorefrontStockReader(dynamoDb);

        foreach (var receipt in InventoryInitialData.Receipts)
        {
            var result = await receive.Handle(new RecordReceiptCommand(
                SeedKey(receipt.ProductId), receipt.ProductId, InventoryInitialData.AlphaId, receipt.Quantity,
                InvoiceNumber: "DEV-SEED", Supplier: null, Note: null, Actor: "seeder"), ct);
            if (result.Refusal is { } refusal)
                throw new InvalidOperationException($"Seed receipt for {receipt.ProductId} refused: {refusal.ErrorType}");

            await ApplyStorefrontTotalAsync(dynamoDb, receipt.ProductId, await totals.TotalForAsync(receipt.ProductId, ct), ct);
        }
    }

    // EventBridge is best-effort locally, so — like local.ts after a movement — this applies the total
    // the way catalogview-inventory-sync-consumer does (ADR-0052 §8). Runs after CatalogView's backfill,
    // which rewrites whole items.
    private static async Task ApplyStorefrontTotalAsync(
        IAmazonDynamoDB dynamoDb, string productId, StorefrontTotal total, CancellationToken ct)
    {
        try
        {
            await dynamoDb.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = "catalogview-products",
                Key = new() { ["Id"] = new AttributeValue { S = productId } },
                UpdateExpression = "SET Stock = :available, StockVersion = :version",
                ConditionExpression = "attribute_not_exists(StockVersion) OR StockVersion < :version",
                ExpressionAttributeValues = new()
                {
                    [":available"] = new AttributeValue { N = total.Available.ToString(CultureInfo.InvariantCulture) },
                    [":version"] = new AttributeValue { N = total.Version.ToString(CultureInfo.InvariantCulture) },
                },
            }, ct);
        }
        catch (ConditionalCheckFailedException)
        {
            // Already holds this total or a newer one.
        }
    }

    private static string SeedKey(string productId) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"inventory-seed:{productId}")).AsSpan(0, 16)).ToString();

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
