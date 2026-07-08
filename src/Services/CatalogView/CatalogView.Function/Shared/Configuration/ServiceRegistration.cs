using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CategorySync;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.PriceChanged;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ProductSync;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewCreated;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewUpdated;
using CatalogView.Function.Modules.Products.Queries.GetProduct;
using CatalogView.Function.Modules.Products.Queries.SearchProducts;
using OpenSearch.Net;

namespace CatalogView.Function.Shared.Configuration;

public static class ServiceRegistration
{
    // Free-tier single-node domain (ADR-0027) — no Aspire OpenSearch hosting package exists yet,
    // so local dev uses a raw container (AppHost/CatalogViewExtensions.cs) exposing this endpoint.
    private const string DefaultLocalEndpoint = "http://localhost:9200";

    public static IServiceCollection AddCatalogViewServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOpenSearchLowLevelClient>(_ =>
        {
            var endpoint = configuration["OpenSearch:Endpoint"] ?? DefaultLocalEndpoint;
            var pool = new SingleNodeConnectionPool(new Uri(endpoint));
            var settings = new ConnectionConfiguration(pool);
            return new OpenSearchLowLevelClient(settings);
        });

        services.AddScoped<IProductSearchIndex, OpenSearchProductIndex>();

        services.AddScoped<CatalogProductSyncHandler>();
        services.AddScoped<CategorySyncHandler>();
        services.AddScoped<PriceSyncHandler>();
        services.AddScoped<ReviewAggregateHandler>();
        services.AddScoped<ReviewUpdateAggregateHandler>();
        services.AddScoped<SearchProductsHandler>();
        services.AddScoped<GetProductHandler>();

        return services;
    }
}
