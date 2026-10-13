using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;
using static AppHost.Extensions.Extensions;

namespace AppHost.Inventory;

public record InventoryResources(
    IResourceBuilder<ProjectResource> Seeder,
    IReadOnlyList<IResourceBuilder<LambdaProjectResource>> MovementLambdas);

public static class InventoryExtensions
{
    public static InventoryResources AddInventoryServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        // Creates inventory-warehouses, inventory-stock and inventory-movements and seeds ALPHA/BETA.
        var seeder = builder.AddProject<Projects.Inventory_DevelopmentDataSeeder>("inventory-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // The stock movements (ADR-0052 §7). Invoked by the SPA's local.ts through the Lambda emulator,
        // standing in for the AppSync mutations; the warehouse fields need no Lambda (local.ts serves
        // them from DynamoDB Local).
        var movementLambdas = new (string Name, string Handler)[]
            {
                ("inventory-record-stock-receipt", "RecordStockReceipt"),
                ("inventory-record-stock-adjustment", "RecordStockAdjustment"),
                ("inventory-transfer-stock", "TransferStock"),
            }
            .Select(lambda => builder.AddAWSLambdaFunction<Projects.Inventory_Function>(
                    lambda.Name,
                    lambdaHandler: LambdaHandler("Inventory.Function", lambda.Handler))
                .WaitForCompletion(seeder)
                .WithReference(dynamoDb)
                .WithAwsDevEnvironment())
            .ToList();

        // The storefront total's CDC publishers (ADR-0052 §8): DynamoDB Streams in, EventBridge out
        // (best-effort locally, so local.ts applies the total to catalogview-products itself).
        foreach (var (name, handler, table) in new[]
                 {
                     ("inventory-stock-stream-publisher", "InventoryStockStreamPublisher", "inventory-stock"),
                     ("inventory-warehouses-stream-publisher", "InventoryWarehousesStreamPublisher", "inventory-warehouses"),
                 })
        {
            builder.AddAWSLambdaFunction<Projects.Inventory_Function>(
                    name,
                    lambdaHandler: LambdaHandler("Inventory.Function", handler))
                .WaitForCompletion(seeder)
                .WithReference(dynamoDb)
                .WithDynamoDBStreamsEventSource(table)
                .WithAwsDevEnvironment()
                .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");
        }

        return new InventoryResources(seeder, movementLambdas);
    }
}
