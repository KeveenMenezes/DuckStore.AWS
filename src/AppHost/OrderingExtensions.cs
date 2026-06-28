using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;

namespace AppHost.Ordering;

public record OrderingResources(
    IResourceBuilder<LambdaProjectResource> GetOrdersByCustomer,
    IResourceBuilder<LambdaProjectResource> DeleteOrder
);

public static class OrderingExtensions
{
    private const string OrderingTableName = "ordering";

    public static OrderingResources AddOrderingServices(
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
                lambdaHandler: "Ordering.Function::Ordering.Function.EventsIntegration.Consumer.BasketCheckoutConsumerFunction::FunctionHandler")
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-order-created-publisher",
                lambdaHandler:
                "Ordering.Function::Ordering.Function.EventsIntegration.Publisher.OrderCreatedPublisherFunction::FunctionHandler")
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(OrderingTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        var getOrdersByCustomer = builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-get-orders-by-customer",
                lambdaHandler: "Ordering.Function::Ordering.Function.Functions_GetOrdersByCustomer_Generated::GetOrdersByCustomer")
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        var deleteOrder = builder.AddAWSLambdaFunction<Projects.Ordering_Function>(
                "ordering-delete-order",
                lambdaHandler: "Ordering.Function::Ordering.Function.Functions_DeleteOrder_Generated::DeleteOrder")
            .WaitForCompletion(orderingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return new OrderingResources(getOrdersByCustomer, deleteOrder);
    }
}
