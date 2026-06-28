using Amazon.DynamoDBv2;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Idempotency;
using BuildingBlocks.ServiceDefaults.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.Function.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (HTTP endpoints),
// the EventBridge consumer, the DynamoDB Streams publisher, and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddOrderingServices(
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
        services.AddScoped<IOrderRepository, DynamoOrderRepository>();

        services.AddEventBridgeMessaging(configuration);
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        return services;
    }
}
