using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using static AppHost.Extensions.Extensions;

namespace AppHost.Ordering;

public static class OrderingExtensions
{
    private const string OrderingTableName = "ordering";

    // ordersByCustomer (read) and deleteOrder (delete) are AppSync direct DynamoDB resolvers
    // (ADR-0009), not Lambdas — so Ordering only registers its two event-driven Lambdas here.
    public static void AddOrderingServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<ElasticsearchResource> elasticsearch)
    {
        var orderingMigration = builder.AddProject<Projects.Ordering_DevelopmentDataSeeder>("ordering-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-basket-checkout-consumer",
                lambdaHandler: LambdaHandler("Ordering.Function", "BasketCheckoutConsumer"))
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-stream-publisher",
                lambdaHandler:
                LambdaHandler("Ordering.Function", "OrderStreamPublisher"))
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(OrderingTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-payment-authorized-consumer",
                lambdaHandler:
                LambdaHandler("Ordering.Function", "OrderPaymentAuthorizedConsumer"))
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-payment-declined-consumer",
                lambdaHandler:
                LambdaHandler("Ordering.Function", "OrderPaymentDeclinedConsumer"))
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");
    }
}
