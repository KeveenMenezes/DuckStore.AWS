namespace Pricing.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (Lambda-backed
// mutations/queries), the EventBridge consumer, and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddPricingServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();

        var assembly = typeof(ServiceRegistration).Assembly;
        services
            .AddMediatR(config =>
            {
                config.RegisterServicesFromAssembly(assembly);
                config.AddOpenBehavior(typeof(ValidationBehavior<,>));
                config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            })
            .AddValidatorsFromAssembly(assembly);

        services.AddSingleton(InstallmentOptions.FromConfiguration(configuration));

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IPriceRepository, DynamoPriceRepository>();
        services.AddScoped<ICampaignRepository, DynamoCampaignRepository>();
        services.AddScoped<IGatewayCostRepository, DynamoGatewayCostRepository>();

        services.AddEventBridgeMessaging(configuration);
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        return services;
    }
}
