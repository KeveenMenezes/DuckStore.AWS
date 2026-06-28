using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SdkAttribute = Amazon.DynamoDBv2.Model.AttributeValue;

namespace Basket.Function.EventsIntegration.Publisher;

// Triggered by ShoppingCarts DynamoDB Stream. On MODIFY records where Type = "Checkout",
// publishes BasketCheckoutEvent to EventBridge and cleans up the basket item.
// This replaces the previous in-process best-effort publish in CheckoutBasketCommandHandler.
public class ShoppingCartsEventPublisherFunction
{
    private readonly IServiceProvider _serviceProvider;

    public ShoppingCartsEventPublisherFunction()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddEventBridgeMessaging(configuration);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task FunctionHandler(DynamoDBEvent dynamoEvent)
    {
        using var scope = _serviceProvider.CreateScope();
        var dynamoDb = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        foreach (var record in dynamoEvent.Records)
        {
            if (record.EventName != "MODIFY")
                continue;

            if (!record.Dynamodb.NewImage.TryGetValue("Type", out var type) || type.S != "Checkout")
                continue;

            if (!record.Dynamodb.NewImage.TryGetValue("CheckoutData", out var checkoutData))
                continue;

            var checkoutEvent = JsonSerializer.Deserialize<BasketCheckoutEvent>(checkoutData.S)!;
            await eventPublisher.PublishAsync(checkoutEvent);

            var userName = record.Dynamodb.NewImage["UserName"].S;
            await dynamoDb.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = BasketRepository.TableName,
                Key = new Dictionary<string, SdkAttribute> { ["UserName"] = new(userName) }
            });
        }
    }
}
