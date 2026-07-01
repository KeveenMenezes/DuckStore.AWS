using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.EventBridge;
using Catalog.Function.Modules.Products.EventsIntegration.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Function.Modules.Products.EventsIntegration.Publisher;

// Triggered by the Products DynamoDB Stream. Publishes one CatalogUpdatedEvent per record to
// EventBridge. ISR cache revalidation is handled downstream by the SPA's
// SpaRevalidationWebhook Lambda (infra/constructs/spa-revalidation-webhook.ts),
// which subscribes to this same event directly.
public class CatalogStreamEventPublisherFunction
{
    private readonly IServiceProvider _serviceProvider;

    public CatalogStreamEventPublisherFunction()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEventBridgeMessaging(configuration);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task FunctionHandler(DynamoDBEvent dynamoEvent)
    {
        if (dynamoEvent.Records.Count == 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        foreach (var record in dynamoEvent.Records)
        {
            await eventPublisher.PublishAsync(new CatalogUpdatedEvent { ChangeType = record.EventName });
        }
    }
}
