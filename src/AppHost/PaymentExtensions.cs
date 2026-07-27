using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using static AppHost.Extensions.Extensions;

namespace AppHost.Payment;

public static class PaymentExtensions
{
    private const string PaymentsTableName = "payments";

    // PaymentGateway.Function is registered here too rather than in its own extensions file: it
    // owns no DynamoDB table, no seeder, no idempotency inbox (ADR-0025) — a single Lambda
    // registration doesn't warrant a near-empty file of its own.
    public static void AddPaymentServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var paymentMigration = builder.AddProject<Projects.Payment_DevelopmentDataSeeder>("payment-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-basket-checkout-consumer",
                lambdaHandler: LambdaHandler("Payment.Function", "BasketCheckoutConsumer"))
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-payments-stream-publisher",
                lambdaHandler: LambdaHandler("Payment.Function", "PaymentStreamPublisher"))
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(PaymentsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-result-authorized-consumer",
                lambdaHandler: LambdaHandler("Payment.Function", "PaymentAuthorizedConsumer"))
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-result-declined-consumer",
                lambdaHandler: LambdaHandler("Payment.Function", "PaymentDeclinedConsumer"))
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.PaymentGateway_Function>(
                "paymentgateway-payment-requested-consumer",
                lambdaHandler: LambdaHandler("PaymentGateway.Function", "PaymentRequestedConsumer"))
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");
    }
}
