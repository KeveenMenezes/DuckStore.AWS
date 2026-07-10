using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Catalog;

public static class CatalogExtensions
{
    private const string ProductsTableName = "products";
    private const string CategoriesTableName = "categories";

    public static IResourceBuilder<ProjectResource> AddCatalogLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var catalogSeeder = builder.AddProject<Projects.Catalog_DevelopmentDataSeeder>("catalog-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-stream-event-publisher",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_ProductStreamPublisher_Generated::ProductStreamPublisher")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ProductsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // ISR revalidation for catalog changes is production-only (SpaTagRevalidator
        // in infra/) — `next dev` doesn't do ISR caching, so there's no local equivalent.

        // Rating aggregation moved to CatalogView's DynamoDB table (ADR-0030, supersedes ADR-0027's
        // OpenSearch design) — the old catalog-review-created-consumer Lambda is gone (ADR-0011
        // §4). ProductStreamPublisher now also emits CatalogProductSyncEvent (CatalogSearchSyncRule)
        // for CatalogView to consume.

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-category-stream-publisher",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_CategoryStreamPublisher_Generated::CategoryStreamPublisher")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(CategoriesTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return catalogSeeder;
    }
}
