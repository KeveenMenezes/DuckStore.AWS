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
        // DynamoDB Local injeta AWS_ENDPOINT_URL_DYNAMODB; o SDK resolve sozinho.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddEventBridgeMessaging(configuration);

        return services;
    }
}