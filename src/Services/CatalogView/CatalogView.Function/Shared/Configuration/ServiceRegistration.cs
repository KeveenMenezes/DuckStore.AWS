using Amazon.DynamoDBv2;

namespace CatalogView.Function.Shared.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddCatalogViewServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own
        // (ADR-0030).
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

        services.AddEventBridgeMessaging(configuration);

        services.AddScoped<IProductSearchIndex, DynamoProductIndex>();

        services.AddScoped<ProductSyncedHandler>();
        services.AddScoped<ProductDeletedHandler>();
        services.AddScoped<CategorySyncHandler>();
        services.AddScoped<PriceSyncHandler>();
        services.AddScoped<ReviewAggregateHandler>();
        services.AddScoped<ReviewUpdateAggregateHandler>();

        // Stream publisher (ADR-0035) — emits CatalogViewProductSyncedEvent/
        // CatalogViewProductDeletedEvent off catalogview-products writes.
        services.AddScoped<IStreamRule<CatalogViewProductStreamImage>, CatalogViewProductSyncedRule>();
        services.AddScoped<IStreamRule<CatalogViewProductStreamImage>, CatalogViewProductDeletedRule>();
        services.AddScoped<StreamRuleDispatcher<CatalogViewProductStreamImage>>();

        return services;
    }
}
