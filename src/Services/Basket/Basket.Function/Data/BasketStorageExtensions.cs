using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basket.Function.Data;

public static class BasketStorageExtensions
{
    /// <summary>
    /// Registers the basket persistence: <see cref="BasketRepository"/> over DynamoDB, with
    /// no caching layer. Locally, DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB and the SDK
    /// resolves it; on AWS the client uses the Lambda role's credentials/region.
    /// </summary>
    public static IServiceCollection AddBasketStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IBasketRepository, BasketRepository>();
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

        return services;
    }
}
