using BuildingBlocks.Core.Validation;
using Ordering.Function.Modules.Orders.EventsIntegration.Consumers.BasketCheckout;
namespace Ordering.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (HTTP endpoints),
// the EventBridge consumer, the DynamoDB Streams publisher, and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddOrderingServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();

        var assembly = typeof(ServiceRegistration).Assembly;
        // Mediator generates the dispatch table at compile time; AddMediator() is the
        // generated registration, so no assembly is scanned at startup (ADR-0042 §7).
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // Explicit, because there is no scanning left to discover them —
        // the point of dropping FluentValidation (ADR-0042 §7).
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IOrderRepository, DynamoOrderRepository>();

        services.AddEventBridgeMessaging(configuration);
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        // Stream-publisher rules + dispatcher (see ADR-0019). Scoped so a rule can depend on the
        // scoped repository; add a rule per state-change the Orders module needs to publish.
        services.AddScoped<IStreamRule<OrderStreamImage>, OrderCreatedRule>();
        services.AddScoped<StreamRuleDispatcher<OrderStreamImage>>();

        return services;
    }
}
