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

        // Grouped by producer bounded context (ADR-0040), each with its own strategy contract —
        // ICatalogSyncStrategy/CatalogSyncDispatcher and IReviewSyncStrategy/ReviewSyncDispatcher
        // never mix, so a strategy from one producer can never be picked up by the other
        // producer's dispatcher. PriceSyncHandler stays a plain 1:1 handler — Pricing is the only
        // producer with a single occurrence relevant here, so the Strategy pattern buys nothing.
        services.AddScoped<ICatalogSyncStrategy, ProductSyncStrategy>();
        services.AddScoped<ICatalogSyncStrategy, ProductDeleteStrategy>();
        services.AddScoped<ICatalogSyncStrategy, CategorySyncStrategy>();
        services.AddScoped<CatalogSyncDispatcher>();

        services.AddScoped<IReviewSyncStrategy, ReviewCreateStrategy>();
        services.AddScoped<IReviewSyncStrategy, ReviewUpdateStrategy>();
        services.AddScoped<ReviewSyncDispatcher>();

        services.AddScoped<PriceSyncHandler>();

        // Stream publisher (ADR-0035) — emits CatalogViewProductSyncedEvent/
        // CatalogViewProductDeletedEvent off catalogview-products writes.
        services.AddScoped<IStreamRule<CatalogViewProductStreamImage>, CatalogViewProductSyncedRule>();
        services.AddScoped<IStreamRule<CatalogViewProductStreamImage>, CatalogViewProductDeletedRule>();
        services.AddScoped<StreamRuleDispatcher<CatalogViewProductStreamImage>>();

        return services;
    }
}
