using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Catalog;

public static class CatalogExtensions
{
    private const string ProductsTableName = "Products";
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
                "catalog-create-product",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_CreateProduct_Generated::CreateProduct")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-update-product",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_UpdateProduct_Generated::UpdateProduct")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-delete-product",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_DeleteProduct_Generated::DeleteProduct")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_StreamEventPublish_Lambda>(
                "catalog-stream-event-publisher",
                lambdaHandler: "Catalog.StreamEventPublish.Lambda::Catalog.StreamEventPublish.Lambda.Function::FunctionHandler")
            .WaitForCompletion(catalogSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ProductsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("Catalog__WebhookUrl", $"{spaWebhookUrl}/api/webhooks/catalog-updated")
            .WithEnvironment("CATALOG_WEBHOOK_SECRET", CatalogWebhookSecret);

        return builder;
    }

    public static string CatalogWebhookSecretValue => CatalogWebhookSecret;
}
