using Amazon.Lambda;
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

        // Basket persistence + cache per environment:
        // Redis (cache-aside) in non-production; DynamoDB DAX (transparent) in production.
        services.AddBasketStorage(configuration);

        // Discount Lambda client: direct invocation via the AWS Lambda Invoke API.
        // In local dev, AWS_ENDPOINT_URL_LAMBDA points at the Aspire emulator; the SDK resolves it on its own.
        var discountFunctionName = configuration["Discount:FunctionName"] ?? "discount-get-discount";
        services.AddSingleton<IAmazonLambda>(_ => new AmazonLambdaClient());
        services.AddSingleton<IDiscountClient>(
            sp => new DiscountLambdaClient(sp.GetRequiredService<IAmazonLambda>(), discountFunctionName));
    }
}
