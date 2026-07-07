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
        // No Aspire.Hosting.OpenSearch / CommunityToolkit.Aspire.Hosting.OpenSearch package exists
        // yet — raw container, same pattern as the Kibana container in ObservabilityExtensions.cs.
        // Free-tier discipline (ADR-0027): single node, security plugin disabled for LOCAL DEV
        // ONLY (the real domain uses IAM auth, never this flag).
        // DISABLE_SECURITY_PLUGIN is the docker-entrypoint switch; the dotted setting
        // (plugins.security.disabled) is ignored by the entrypoint, which then demands
        // OPENSEARCH_INITIAL_ADMIN_PASSWORD and aborts the boot.
        // WithHttpHealthCheck is required so WaitFor(opensearch) actually blocks until the
        // cluster answers HTTP — a raw AddContainer resource is otherwise considered "Running"
        // the instant the process starts, well before OpenSearch finishes booting (~15s). Without
        // it, catalogview-data-seeder raced the cluster, crashed, and WaitForCompletion let its
        // dependents (the CatalogView Lambdas) start anyway — leaving the "products" index missing
        // and the storefront's product list empty.
        var opensearch = builder.AddContainer("opensearch", "opensearchproject/opensearch", "2.19.1")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
            .WithEnvironment("DISABLE_INSTALL_DEMO_CONFIG", "true")
            .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithHttpEndpoint(port: 9200, targetPort: 9200, name: "http")
            .WithHttpHealthCheck("/_cluster/health", 200, "http");

        var dashboards = builder.AddContainer(
                "opensearch-dashboards",
                "opensearchproject/opensearch-dashboards",
                "2.19.1")
            .WithEnvironment("OPENSEARCH_HOSTS", "[\"http://opensearch:9200\"]")
            .WithEnvironment("DISABLE_SECURITY_DASHBOARDS_PLUGIN", "true")
            .WithHttpEndpoint(
                port: 5602,
                targetPort: 5601,
                name: "http")
            .WaitFor(opensearch);

        // The backfill scans Catalog's `products`, Review's `reviews` and Pricing's `prices`
        // tables (ADR-0026/0027), which are created by those contexts' seeders — wait for all
        // of them to finish first.
        var catalogViewSeeder = builder
            .AddProject<Projects.CatalogView_DevelopmentDataSeeder>("catalogview-data-seeder")
            .WaitFor(opensearch)
            .WaitFor(dynamoDb)
            .WaitForCompletion(catalogSeeder)
            .WaitForCompletion(reviewSeeder)
            .WaitForCompletion(pricingSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-product-sync-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_CatalogProductSyncConsumer_Generated::CatalogProductSyncConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-review-aggregate-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_ReviewAggregateConsumer_Generated::ReviewAggregateConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-review-update-aggregate-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_ReviewUpdateAggregateConsumer_Generated::ReviewUpdateAggregateConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-search-products",
                lambdaHandler: "CatalogView.Function::CatalogView.Function.Functions_SearchProducts_Generated::SearchProducts")
            .WaitForCompletion(catalogViewSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-price-sync-consumer",
                lambdaHandler:
                "CatalogView.Function::CatalogView.Function.Functions_PriceSyncConsumer_Generated::PriceSyncConsumer")
            .WaitForCompletion(catalogViewSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        builder.AddAWSLambdaFunction<Projects.CatalogView_Function>(
                "catalogview-get-product",
                lambdaHandler: "CatalogView.Function::CatalogView.Function.Functions_GetProduct_Generated::GetProduct")
            .WaitForCompletion(catalogViewSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment(ctx =>
            {
                if (!ctx.ExecutionContext.IsPublishMode)
                    ctx.EnvironmentVariables["OpenSearch__Endpoint"] = opensearch.GetEndpoint("http");
            });

        return builder;
    }
}
