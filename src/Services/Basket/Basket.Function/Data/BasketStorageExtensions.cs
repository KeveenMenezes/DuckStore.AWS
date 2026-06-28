using Amazon.DAX;
using Amazon.DynamoDBv2;
using Amazon.Runtime.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basket.Function.Data;

public static class BasketStorageExtensions
{
    /// <summary>
    /// Registers the basket persistence and cache strategy per environment:
    /// <list type="bullet">
    /// <item>Production: DynamoDB Accelerator (DAX) — transparent cache (read/write-through)
    /// in the data layer itself, removing the need for Redis cache-aside.</item>
    /// <item>Non-production: DynamoDB (Local) + Redis cache-aside via
    /// <see cref="CacheBasketRepository"/>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddBasketStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IBasketRepository, BasketRepository>();

        if (IsProduction(configuration))
        {
            // The DAX client implements IAmazonDynamoDB, so it's a drop-in for the standard
            // client; caching is handled by the DAX cluster (no Redis decorator).
            services.AddSingleton(_ => CreateDaxClient(configuration));
        }
        else
        {
            // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
            services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

            // Redis cache (Aspire injects ConnectionStrings__redis) + cache-aside (decorator).
            var redisConnection = configuration.GetConnectionString("redis") ?? "localhost";
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(redisConnection));

            services.Decorate<IBasketRepository, CacheBasketRepository>();
        }

        return services;
    }

    private static bool IsProduction(IConfiguration configuration)
    {
        var environment =
            configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Development";

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }

    private static IAmazonDynamoDB CreateDaxClient(IConfiguration configuration)
    {
        var endpoint = configuration["Dax:Endpoint"]
            ?? throw new InvalidOperationException(
                "Dax:Endpoint precisa estar configurado em produção (endpoint do cluster DAX).");

        var port = int.TryParse(configuration["Dax:Port"], out var configuredPort)
            ? configuredPort
            : 8111;

        var daxConfig = new DaxClientConfig(endpoint, port)
        {
            AwsCredentials = DefaultAWSCredentialsIdentityResolver.GetCredentials()
        };

        return new ClusterDaxClient(daxConfig);
    }
}
