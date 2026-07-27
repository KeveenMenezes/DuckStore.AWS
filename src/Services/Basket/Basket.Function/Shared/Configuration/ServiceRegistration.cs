using Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;
using Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;
using BuildingBlocks.Core.Validation;
namespace Basket.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (HTTP endpoints)
// and the DynamoDB Streams publisher.
public static class ServiceRegistration
{
    public static IServiceCollection AddBasketServices(
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
        services.AddScoped<IValidator<MergeBasketCommand>, MergeBasketCommandValidator>();
        services.AddScoped<IValidator<CheckoutBasketCommand>, CheckoutBasketCommandValidator>();

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IShoppingCartRepository, DynamoShoppingCartRepository>();

        services.AddEventBridgeMessaging(configuration);

        // Stream-publisher rule + dispatcher (see ADR-0019). Scoped for consistency with the
        // reference pattern, even though CheckoutedRule has no scoped dependencies today.
        services.AddScoped<IStreamRule<ShoppingCartStreamImage>, CheckoutedRule>();
        services.AddScoped<StreamRuleDispatcher<ShoppingCartStreamImage>>();

        return services;
    }
}
