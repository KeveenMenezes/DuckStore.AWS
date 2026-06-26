using Aspire.Hosting.AWS.DynamoDB;
using AppHost.Extensions;

namespace AppHost.Ordering;

public static class OrderingExtensions
{
    private const string OrderingTableName = "OrderingTable";

    public static IResourceBuilder<ProjectResource> AddOrderingServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<ElasticsearchResource> elasticsearch)
    {
        var orderingMigration = builder.AddProject<Projects.Ordering_MigrationService>("ordering-migration")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Ordering_BasketCheckoutConsumer_Lambda>(
                "ordering-basket-checkout-consumer",
                lambdaHandler: "Ordering.BasketCheckoutConsumer.Lambda::Ordering.BasketCheckoutConsumer.Lambda.Function::FunctionHandler")
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Ordering_OrderCreatedPublisher_Lambda>(
                "ordering-order-created-publisher",
                lambdaHandler:
                "Ordering.OrderCreatedPublisher.Lambda::Ordering.OrderCreatedPublisher.Lambda.Function::FunctionHandler")
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(OrderingTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        return builder.AddProject<Projects.Ordering_API>("ordering-api")
            .WaitForCompletion(orderingMigration)
            .WaitFor(dynamoDb)
            .WaitFor(elasticsearch)
            .WithReference(dynamoDb)
            .WithReference(elasticsearch)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithHttpHealthCheck("/health");
    }
}
