using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using BuildingBlocks.Messaging.Events;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Configuration;
using Ordering.Application.Orders.Mapping;
using Ordering.Infrastructure.Configuration;
using Ordering.Infrastructure.Data;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Ordering.BasketCheckoutConsumer.Lambda;

// Triggered by the BasketCheckoutEvent on EventBridge. Turns a basket checkout into a CreateOrder
// command, with idempotency (inbox pattern) backed by the ProcessedIntegrationEvents table so a
// redelivered event is processed at most once.
public class Function
{
    private readonly IServiceProvider _serviceProvider;

    public Function()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplicationServices(configuration);
        services.AddInfrastructureServices(configuration);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task FunctionHandler(EventBridgeEvent<BasketCheckoutEvent> evt)
    {
        using var scope = _serviceProvider.CreateScope();
        var dynamoDb = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        if (await IsAlreadyProcessedAsync(dynamoDb, evt.Id))
            return;

        var command = BasketCheckoutMapper.ToCreateOrderCommand(evt.Detail);
        await sender.Send(command);

        await MarkAsProcessedAsync(dynamoDb, evt.Id);
    }

    private static async Task<bool> IsAlreadyProcessedAsync(IAmazonDynamoDB dynamoDb, string messageId)
    {
        var response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = ProcessedIntegrationEvent.TableName,
            Key = new Dictionary<string, AttributeValue> { ["MessageId"] = new(messageId) }
        });

        return response.Item.Count > 0;
    }

    private static Task MarkAsProcessedAsync(IAmazonDynamoDB dynamoDb, string messageId) =>
        dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = ProcessedIntegrationEvent.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["MessageId"] = new(messageId),
                ["ProcessedOnUtc"] = new(DateTime.UtcNow.ToString("O"))
            }
        });

    private static async Task Main()
    {
        Func<EventBridgeEvent<BasketCheckoutEvent>, Task> handler = new Function().FunctionHandler;

        using var bootstrap = LambdaBootstrapBuilder.Create(
                handler,
                new DefaultLambdaJsonSerializer())
            .Build();

        await bootstrap.RunAsync();
    }
}