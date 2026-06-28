using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.EventBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Function.Configuration;

namespace Ordering.Function.EventsIntegration.Publisher;

// Triggered by the ordering DynamoDB Stream. Replaces the outbox pattern: the Order write
// itself is the source of truth for the event — no outbox table or special atomicity between
// the Order and its publication is needed. Gated by the OrderFulfillment feature flag.
public class OrderCreatedPublisherFunction
{
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _orderFulfillmentEnabled;

    public OrderCreatedPublisherFunction()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrderingServices(configuration);

        _serviceProvider = services.BuildServiceProvider();
        _orderFulfillmentEnabled = bool.TryParse(
            configuration["FeatureManagement:OrderFullfilment"], out var enabled) && enabled;
    }

    public async Task FunctionHandler(DynamoDBEvent dynamoEvent)
    {
        if (!_orderFulfillmentEnabled)
            return;

        using var scope = _serviceProvider.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        foreach (var record in dynamoEvent.Records)
        {
            if (record.EventName != "INSERT")
                continue;

            if (!record.Dynamodb.NewImage.TryGetValue("Type", out var type) || type.S != "Order")
                continue;

            var orderId = Guid.Parse(record.Dynamodb.NewImage["Id"].S);
            var order = await orderRepository.GetByIdAsync(orderId);

            if (order is not null)
                await eventPublisher.PublishAsync(order.ToOrderCreatedEvent());
        }
    }
}
