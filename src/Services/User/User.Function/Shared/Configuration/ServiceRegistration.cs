using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults.Behaviors;
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
        services.AddScoped<IUserProfileRepository, DynamoUserProfileRepository>();

        return services;
    }
}
