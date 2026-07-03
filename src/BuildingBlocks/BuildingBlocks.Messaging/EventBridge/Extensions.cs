namespace BuildingBlocks.Messaging.EventBridge;

public static class Extensions
{
    public static IServiceCollection AddEventBridgeMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EventBridgePublisher depends on ILogger<T>; registering logging here keeps this
        // registration self-sufficient (callers don't have to remember to AddLogging()).
        services.AddLogging();

        services.Configure<EventBridgeOptions>(
            configuration.GetSection(EventBridgeOptions.SectionName));

        // The EventBridge bus only exists on AWS — the client resolves the real endpoint via
        // environment/IAM. There is no bus locally, and the publisher publishes best-effort.
        services.AddSingleton<IAmazonEventBridge>(_ => new AmazonEventBridgeClient());

        services.AddSingleton<IEventPublisher, EventBridgePublisher>();

        return services;
    }
}
