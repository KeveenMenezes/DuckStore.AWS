namespace Basket.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (HTTP endpoints)
// and the DynamoDB Streams publisher.
public static class ServiceRegistration
{
    public static IServiceCollection AddBasketServices(
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

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IShoppingCartRepository, DynamoShoppingCartRepository>();

        services.AddEventBridgeMessaging(configuration);

        // Stream-publisher rule + dispatcher (see ADR-0019). Scoped for consistency with the
        // reference pattern, even though CheckoutedRule has no scoped dependencies today.
        services.AddScoped<IStreamRule<ShoppingCartStreamImage>, CheckoutedRule>();
        services.AddScoped<StreamRuleDispatcher<ShoppingCartStreamImage>>();

        return services;
    }
}
