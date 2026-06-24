using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catalog.Function;

[Amazon.Lambda.Annotations.LambdaStartup]
public class Startup
{
    /// <summary>
    /// Services for Lambda functions can be registered in the services dependency injection container in this method.
    ///
    /// The services can be injected into the Lambda function through the containing type's constructor or as a
    /// parameter in the Lambda function using the FromService attribute. Services injected for the constructor have
    /// the lifetime of the Lambda compute container. Services injected as parameters are created within the scope
    /// of the function invocation.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // Here we'll add an instance of our calculator service that will be used by each function
        services.AddSingleton<ICalculatorService>(new CalculatorService());

        services.AddLogging();

        var assembly = typeof(Startup).Assembly;
        services
            .AddMediatR(config =>
            {
                config.RegisterServicesFromAssembly(assembly);
                config.AddOpenBehavior(typeof(ValidationBehavior<,>));
                config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            })
            .AddValidatorsFromAssembly(assembly);

        // DynamoDB Local injeta AWS_ENDPOINT_URL_DYNAMODB; o SDK resolve sozinho.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddSingleton<IProductRepository, DynamoProductRepository>();
        services.AddSingleton<ICategoryRepository, DynamoCategoryRepository>();
    }
}
