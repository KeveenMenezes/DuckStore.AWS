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
        // the three never mix, so a strategy from one producer can never be picked up by another
        // producer's dispatcher. Pricing joined them in ADR-0044: it produced a single occurrence
        // (PriceChangedEvent) and was allowed a plain 1:1 handler until ProductDiscountChangedEvent
        // became its second, which is the trigger ADR-0040 names for owning a strategy pair.
        services.AddScoped<ICatalogSyncStrategy, ProductSyncStrategy>();
        services.AddScoped<ICatalogSyncStrategy, ProductDeleteStrategy>();
        services.AddScoped<ICatalogSyncStrategy, CategorySyncStrategy>();
        services.AddScoped<CatalogSyncDispatcher>();

        services.AddScoped<IReviewSyncStrategy, ReviewCreateStrategy>();
        services.AddScoped<IReviewSyncStrategy, ReviewUpdateStrategy>();
        services.AddScoped<ReviewSyncDispatcher>();

        services.AddScoped<IPricingSyncStrategy, PriceChangedStrategy>();
        services.AddScoped<IPricingSyncStrategy, ProductDiscountChangedStrategy>();
        services.AddScoped<PricingSyncDispatcher>();

        // Stream publisher (ADR-0035) — emits CatalogViewProductSyncedEvent/
        // CatalogViewProductDeletedEvent off catalogview-products writes.
        services.AddScoped<IStreamRule<CatalogViewProductStreamImage>, CatalogViewProductSyncedRule>();
        services.AddScoped<IStreamRule<CatalogViewProductStreamImage>, CatalogViewProductDeletedRule>();
        services.AddScoped<StreamRuleDispatcher<CatalogViewProductStreamImage>>();

        return services;
    }
}
