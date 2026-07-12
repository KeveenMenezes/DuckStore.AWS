namespace Catalog.Function.Shared.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddCatalogServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(ServiceRegistration).Assembly;
        services
            .AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            })
            .AddValidatorsFromAssembly(assembly);

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
