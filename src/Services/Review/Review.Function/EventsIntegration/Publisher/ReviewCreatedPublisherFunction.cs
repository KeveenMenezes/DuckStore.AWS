using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Review.Function.EventsIntegration.Publisher;

// Triggered by the reviews DynamoDB Stream. For each INSERT it publishes a ReviewCreated
// integration event to EventBridge so the Catalog service can fold the rating into the
// product's AverageRating/RatingCount (CDC — ADR-0005/0008/0011). Only INSERTs matter:
// a new review is the only thing that changes a product's aggregate rating here.
public class ReviewCreatedPublisherFunction
{
    private readonly IServiceProvider _serviceProvider;

    public ReviewCreatedPublisherFunction()
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
            if (record.EventName != "INSERT")
                continue;

            var image = record.Dynamodb.NewImage;

            await eventPublisher.PublishAsync(new ReviewCreatedEvent
            {
                ReviewId = Guid.Parse(image["Id"].S),
                ProductId = Guid.Parse(image["ProductId"].S),
                Rating = int.Parse(image["Rating"].N)
            });
        }
    }
}
