using BuildingBlocks.ServiceDefaults.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basket.Function;

[LambdaStartup]
public class Startup
{
    /// <summary>
    /// Services for Lambda functions can be registered in the services dependency injection container in this method.
    /// The services can be injected into the Lambda function through the containing type's constructor or as a
    /// parameter in the Lambda function using the FromService attribute. Services injected for the constructor have
    /// the lifetime of the Lambda compute container. Services injected as parameters are created within the scope
    /// of the function invocation.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();

        // Aspire injects the configuration via environment variables (ConnectionStrings__*, services__*).
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var assembly = typeof(Startup).Assembly;
        services
            .AddMediatR(config =>
            {
                config.RegisterServicesFromAssembly(assembly);
                config.AddOpenBehavior(typeof(ValidationBehavior<,>));
                config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            })
            .AddValidatorsFromAssembly(assembly);

        // Basket persistence: DynamoDB repository, no caching layer.
        services.AddBasketStorage(configuration);

        // Discount was merged into Basket: coupons are read in-process from DynamoDB,
        // replacing the previous cross-service Lambda invoke of the Discount function.
        services.AddSingleton<ICouponRepository, DynamoCouponRepository>();
    }
}
