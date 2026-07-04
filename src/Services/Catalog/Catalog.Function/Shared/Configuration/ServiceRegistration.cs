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
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        services.AddScoped<ReviewCreatedHandler>();

        services.AddScoped<IStreamRule<CatalogStreamImage>, CatalogProductChangedRule>();
        services.AddScoped<StreamRuleDispatcher<CatalogStreamImage>>();

        return services;
    }
}
