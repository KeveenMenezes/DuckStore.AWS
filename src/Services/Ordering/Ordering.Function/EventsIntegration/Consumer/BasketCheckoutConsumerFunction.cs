using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Function.Configuration;

namespace Ordering.Function.EventsIntegration.Consumer;

// Triggered by the BasketCheckoutEvent on EventBridge. Turns a basket checkout into an Order
// write, with idempotency backed by the ordering-processed-events table so a redelivered event
// is processed at most once. The processed-event registration and the order write happen in a
// single TransactWriteItems — no partial state is possible.
public class BasketCheckoutConsumerFunction
{
    private readonly IServiceProvider _serviceProvider;

    public BasketCheckoutConsumerFunction()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrderingServices(configuration);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task FunctionHandler(EventBridgeEvent<BasketCheckoutEvent> evt)
    {
        using var scope = _serviceProvider.CreateScope();
        var consumer = scope.ServiceProvider.GetRequiredService<IIdempotentEventConsumer>();

        var command = BasketCheckoutMapper.ToCreateOrderCommand(evt.Detail);
        var order = CreateOrderHandler.CreateNewOrder(command);

        await consumer.ConsumeAsync(evt.Id, [DynamoOrderRepository.ToTransactWriteItem(order)]);
    }
}
