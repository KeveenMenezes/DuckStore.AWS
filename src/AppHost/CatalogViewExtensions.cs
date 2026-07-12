using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

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

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-product-sync-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_CatalogProductSyncConsumer_Generated::CatalogProductSyncConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-review-aggregate-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_ReviewAggregateConsumer_Generated::ReviewAggregateConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-review-update-aggregate-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_ReviewUpdateAggregateConsumer_Generated::ReviewUpdateAggregateConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-price-sync-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_PriceSyncConsumer_Generated::PriceSyncConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-category-sync-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_CategorySyncConsumer_Generated::CategorySyncConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return builder;
    }
}
