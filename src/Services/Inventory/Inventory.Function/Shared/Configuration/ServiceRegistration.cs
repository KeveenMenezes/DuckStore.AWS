using Inventory.Function.Modules.Stock.EventsIntegration.Publishers;
using Inventory.Function.Modules.Stock.EventsIntegration.Publishers.Rules;
using Inventory.Function.Modules.Stock.Features;
using Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers;
using Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers.Rules;

namespace Inventory.Function.Shared.Configuration;

// Single DI composition for the service's Lambdas (the [LambdaStartup]).
public static class ServiceRegistration
{
    public static IServiceCollection AddInventoryServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();

        // Mediator generates the dispatch table at compile time; AddMediator() is the generated
        // registration, so no assembly is scanned at startup (ADR-0042 §7). Input rules live in the
        // domain and come back as refusals (ADR-0052 §7), so there is no validation behavior here.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IWarehouseReader, DynamoWarehouseReader>();
        services.AddScoped<IStockRepository, DynamoStockRepository>();
        services.AddScoped<MovementRecorder>();

        // The storefront total's CDC publishers (ADR-0052 §8).
        services.AddEventBridgeMessaging(configuration);
        services.AddScoped<IStorefrontStockReader, DynamoStorefrontStockReader>();
        services.AddScoped<IStreamRule<StockStreamImage>, ProductStockChangedRule>();
        services.AddScoped<StreamRuleDispatcher<StockStreamImage>>();
        services.AddScoped<IStreamFanOutRule<WarehouseStreamImage>, WarehouseActivationRule>();
        services.AddScoped<StreamRuleDispatcher<WarehouseStreamImage>>();

        return services;
    }
}
