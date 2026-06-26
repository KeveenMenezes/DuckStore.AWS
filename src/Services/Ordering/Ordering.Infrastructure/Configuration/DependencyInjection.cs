using Amazon.DynamoDBv2;
using BuildingBlocks.Messaging.EventBridge;
using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

namespace Ordering.Infrastructure.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddEventBridgeMessaging(configuration);

        return services;
    }
}