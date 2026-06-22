using Amazon.EventBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.EventBridge;

public static class Extensions
{
    public static IServiceCollection AddEventBridgeMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EventBridgeOptions>(
            configuration.GetSection(EventBridgeOptions.SectionName));

        // O bus EventBridge só existe na AWS — o cliente resolve o endpoint real via
        // ambiente/IAM. Localmente não há bus, e o publisher publica em best-effort.
        services.AddSingleton<IAmazonEventBridge>(_ => new AmazonEventBridgeClient());

        services.AddSingleton<IEventPublisher, EventBridgePublisher>();

        return services;
    }
}
