using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults.Lambda.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace User.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddUserServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();

        var assembly = typeof(ServiceRegistration).Assembly;
        // Mediator generates the dispatch table at compile time; AddMediator() is the
        // generated registration, so no assembly is scanned at startup (ADR-0042 §7).
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IUserProfileRepository, DynamoUserProfileRepository>();

        return services;
    }
}
