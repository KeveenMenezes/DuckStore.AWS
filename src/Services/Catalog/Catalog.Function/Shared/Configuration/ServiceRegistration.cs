namespace Catalog.Function.Shared.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddCatalogServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(ServiceRegistration).Assembly;
        // Mediator generates the dispatch table at compile time; AddMediator() is the
        // generated registration, so no assembly is scanned at startup (ADR-0042 §7).
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IProductRepository, DynamoProductRepository>();
        services.AddScoped<ICategoryRepository, DynamoCategoryRepository>();

        services.AddEventBridgeMessaging(configuration);

        // Rating aggregation moved to CatalogView — Catalog no longer needs an
        // idempotent-consumer inbox (ADR-0027, supersedes ADR-0011 §4).
        services.AddScoped<IStreamRule<CatalogStreamImage>, ProductCreatedRule>();
        services.AddScoped<IStreamRule<CatalogStreamImage>, ProductUpdatedRule>();
        services.AddScoped<IStreamRule<CatalogStreamImage>, ProductDeletedRule>();
        services.AddScoped<IStreamRule<CatalogStreamImage>, ProductSyncedRule>();
        services.AddScoped<StreamRuleDispatcher<CatalogStreamImage>>();

        services.AddScoped<IStreamRule<CategoryStreamImage>, CategorySyncRule>();
        services.AddScoped<StreamRuleDispatcher<CategoryStreamImage>>();

        return services;
    }
}
