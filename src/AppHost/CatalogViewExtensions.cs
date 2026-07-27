using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using static AppHost.Extensions.Extensions;

namespace AppHost.CatalogView;

public static class CatalogViewExtensions
{
    public static IDistributedApplicationBuilder AddCatalogViewLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<ProjectResource> catalogSeeder,
        IResourceBuilder<ProjectResource> reviewSeeder,
        IResourceBuilder<ProjectResource> pricingSeeder)
    {
        // CatalogView owns "catalogview-products" (ADR-0030) — provisioned by the seeder's
        // DynamoTableInitializer, same pattern as every other service's local table (see
        // OrderingExtensions.cs).
        // The backfill scans Catalog's `products`, Review's `reviews` and Pricing's `prices`
        // tables (ADR-0026/0027), which are created by those contexts' seeders — wait for all
        // of them to finish first.
        var catalogViewSeeder = builder
            .AddProject<Projects.CatalogView_DevelopmentDataSeeder>("catalogview-data-seeder")
            .WaitFor(dynamoDb)
            .WaitForCompletion(catalogSeeder)
            .WaitForCompletion(reviewSeeder)
            .WaitForCompletion(pricingSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // Grouped by producer bounded context (ADR-0040): Catalog and Review each dispatch 2+
        // detail-types to one Lambda via SyncStrategyDispatcher; Pricing stays a plain 1:1 consumer.
        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-catalog-sync-consumer",
                lambdaHandler:
                LambdaHandler("CatalogView.Function", "CatalogSyncConsumer"))
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-review-sync-consumer",
                lambdaHandler:
                LambdaHandler("CatalogView.Function", "ReviewSyncConsumer"))
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-price-sync-consumer",
                lambdaHandler:
                LambdaHandler("CatalogView.Function", "PriceSyncConsumer"))
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return builder;
    }
}
