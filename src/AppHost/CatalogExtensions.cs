using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Catalog;

public static class CatalogExtensions
{
    private const string ProductsTableName = "products";
    private const string CatalogWebhookSecret = "catalog-dev-secret";

    public static IDistributedApplicationBuilder AddCatalogLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        string spaWebhookUrl)
    {
        var catalogSeeder = builder.AddProject<Projects.Catalog_DevelopmentDataSeeder>("catalog-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-stream-event-publisher",
                lambdaHandler: "Catalog.Function::Catalog.Function.EventsIntegration.Publisher.CatalogStreamEventPublisherFunction::FunctionHandler")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ProductsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // Consumes CatalogUpdated from EventBridge and triggers ISR cache revalidation on the SPA.
        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-catalog-updated-consumer",
                lambdaHandler: "Catalog.Function::Catalog.Function.EventsIntegration.Consumer.CatalogUpdatedConsumerFunction::FunctionHandler")
            .WaitForCompletion(catalogSeeder)
            .WithAwsDevEnvironment()
            .WithEnvironment("Catalog__WebhookUrl", $"{spaWebhookUrl}/api/webhooks/catalog-updated")
            .WithEnvironment("CATALOG_WEBHOOK_SECRET", CatalogWebhookSecret);

        // Consumes ReviewCreated from EventBridge and folds the rating into the product
        // (AverageRating/RatingCount) — ADR-0011.
        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-review-created-consumer",
                lambdaHandler: "Catalog.Function::Catalog.Function.EventsIntegration.Consumer.ReviewCreatedConsumerFunction::FunctionHandler")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return builder;
    }

    public static string CatalogWebhookSecretValue => CatalogWebhookSecret;
}
