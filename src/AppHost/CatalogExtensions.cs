using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Catalog;

public static class CatalogExtensions
{
    private const string ProductsTableName = "products";

    public static IDistributedApplicationBuilder AddCatalogLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var catalogSeeder = builder.AddProject<Projects.Catalog_DevelopmentDataSeeder>("catalog-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-stream-event-publisher",
                lambdaHandler: "Catalog.Function::Catalog.Function.Modules.Products.EventsIntegration.Publisher.CatalogStreamEventPublisherFunction::FunctionHandler")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ProductsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // ISR revalidation for catalog changes is production-only (SpaRevalidationWebhook
        // in infra/) — `next dev` doesn't do ISR caching, so there's no local equivalent.

        // Consumes ReviewCreated from EventBridge and folds the rating into the product
        // (AverageRating/RatingCount) — ADR-0011.
        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-review-created-consumer",
                lambdaHandler: "Catalog.Function::Catalog.Function.Modules.Products.EventsIntegration.Consumer.ReviewCreatedConsumerFunction::FunctionHandler")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return builder;
    }
}
