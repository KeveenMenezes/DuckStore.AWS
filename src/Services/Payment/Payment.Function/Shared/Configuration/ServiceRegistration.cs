namespace Payment.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] consumers, the
// DynamoDB Streams publisher, and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddPaymentServices(
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
        services.AddScoped<IPaymentRepository, DynamoPaymentRepository>();

        services.AddEventBridgeMessaging(configuration);
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        // Stream-publisher rules + dispatcher (see ADR-0019). Scoped so a rule can depend on the
        // scoped repository; add a rule per state-change the Payments module needs to publish.
        services.AddScoped<IStreamRule<PaymentStreamImage>, PaymentRequestedRule>();
        services.AddScoped<StreamRuleDispatcher<PaymentStreamImage>>();

        return services;
    }
}
