using Amazon.DynamoDBv2;

namespace BuildingBlocks.Messaging.Idempotency;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddIdempotentEventConsumer(
        this IServiceCollection services, string tableName) =>
        services.AddSingleton<IIdempotentEventConsumer>(sp =>
            new DynamoIdempotentEventConsumer(
                sp.GetRequiredService<IAmazonDynamoDB>(), tableName));
}
