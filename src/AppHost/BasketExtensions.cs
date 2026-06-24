using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.AWS.DynamoDB;
using AppHost.Extensions;

namespace AppHost.Basket;

public static class BasketExtensions
{
    public static IResourceBuilder<ProjectResource> AddBasketApi(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<RedisResource> redis,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<ProjectResource> discountApi,
        IResourceBuilder<ElasticsearchResource> elasticsearch)
    {
        var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
            .WaitFor(redis)
            .WaitFor(dynamoDb)
            .WaitFor(discountApi)
            .WaitFor(elasticsearch)
            .WithReference(redis)
            .WithReference(dynamoDb)
            .WithReference(discountApi)
            .WithReference(elasticsearch)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithHttpHealthCheck("/health");

        redis.WithParentRelationship(basketApi);

        return basketApi;
    }
}
